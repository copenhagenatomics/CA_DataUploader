#nullable enable
using CA.LoopControlPluginBase;
using CA_DataUploaderLib.Extensions;
using CA_DataUploaderLib.Helpers;
using CA_DataUploaderLib.IOconf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace CA_DataUploaderLib
{
    public sealed class CommandHandler : IDisposable
    {
        private readonly IIOconf _ioconf;
        private readonly SerialNumberMapper? _mapper;
        private SerialNumberMapper Mapper => _mapper ?? throw new NotSupportedException("Usage of SerialNumberMapper detected on an unsupported context");
        private readonly ICommandRunner _commandRunner;
        private DateTime _start = DateTime.UtcNow;
        private readonly StringBuilder inputCommand = new();
        private readonly List<string> AcceptedCommands = [];
        private readonly List<ISubsystemWithVectorData> _subsystems = [];
        private int AcceptedCommandsIndex = -1;
        private readonly List<LoopControlDecision> _decisions = [];
        private readonly List<LoopControlDecision> _safetyDecisions = [];
        private readonly Lazy<ExtendedVectorDescription> _fullSystemFilterAndMath;
        private readonly CancellationTokenSource _exitCts = new();
        private readonly TaskCompletionSource _runningTaskTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly bool _isMultipi;
        private readonly List<ChannelWriter<DataVector>> _receivedVectorsWriters = [];
        private readonly List<DataVectorReader> _receivedVectorsReaders = [];
        private readonly Channel<EventFiredArgs> _locallyFiredEvents = Channel.CreateBounded<EventFiredArgs>(
            new BoundedChannelOptions(200) { FullMode = BoundedChannelFullMode.DropOldest });
        /// <remarks>
        /// This method is not thread safe, so in general call it from the main thread/async flow before the program cycles are started.
        /// The only additional cycle that is allowed to call it is the decision cycle *before* the first decision.
        /// 
        /// For subsystems, this is usually the constructor of the subsystem or on a handler of <see cref="FullVectorIndexesCreated"/>.
        /// </remarks>
        public DataVectorReader GetReceivedVectorsReader(int capacity = int.MaxValue)
        {
            var channel = capacity == int.MaxValue ? Channel.CreateUnbounded<DataVector>() : Channel.CreateBounded<DataVector>(new BoundedChannelOptions(capacity) { FullMode = BoundedChannelFullMode.DropOldest });
            _receivedVectorsWriters.Add(channel.Writer);
            var reader = new DataVectorReader(channel.Reader);
            _receivedVectorsReaders.Add(reader); //kept only to be able to report the latest processed vector times
            return reader;
        }

        public DateTime? LatestVectorTimeProcessedByAllReaders () => 
            _receivedVectorsReaders.OrderBy(r => r.LastVectorTimeProcessed).Select(r => (DateTime?) r.LastVectorTimeProcessed).FirstOrDefault();

        public event EventHandler<EventFiredArgs>? EventFired;
        public event EventHandler<EventFiredArgs>? UserCommandReceived;
        public event EventHandler<IReadOnlyDictionary<string, int>>? FullVectorIndexesCreated;
        public event EventHandler<VectorDescription>? FullVectorDescriptionCreated;
        public bool IsRunning => !_exitCts.IsCancellationRequested;
        public CancellationToken StopToken => _exitCts.Token;
        public Task RunningTask => _runningTaskTcs.Task;
        public IReadOnlyList<LoopControlDecision> Decisions => _decisions;
        public TimeProvider Time { get; }
        public ILog Logger { get; }

        public CommandHandler(IIOconf ioconf, SerialNumberMapper? mapper = null, ICommandRunner? runner = null, bool runCommandLoop = true, TimeProvider? time = null, ILog? logger = null)
        {
            Time = time ?? TimeProvider.System;
            Logger = logger ?? CALog.Default;
            _exitCts.Token.Register(() =>
            {
                _runningTaskTcs.TrySetCanceled();
                foreach (var writer in _receivedVectorsWriters)                
                    writer.TryComplete();
            });
            _commandRunner = runner ?? new DefaultCommandRunner(Logger);
            _ioconf = ioconf;
            _mapper = mapper;
            _fullSystemFilterAndMath = new Lazy<ExtendedVectorDescription>(GetFullSystemFilterAndMath);
            if (runCommandLoop)
                _ = Task.Run(LoopForever);
            AddCommand("escape", _ => Stop());
            AddCommand("help", HelpMenu);

            _isMultipi = _ioconf.GetEntries<IOconfNode>().Any();
            //we run the command in all nodes
            AddMultinodeCommand("up", _ => true, Uptime);
            AddMultinodeCommand("version", _ => true, GetVersion);
        }

        /// <param name="inputValidation">a validation function that returns the same value regardless of the node in which it runs</param>
        /// <param name="action">an action that can run in all nodes</param>
        public void AddMultinodeCommand(string command, Func<List<string>, bool> inputValidation, Action<List<string>> action)
        {
            if (!_isMultipi)
            {
                AddCommand(command, a => 
                {
                    if (!inputValidation(a)) return false;
                    action(a);
                    return true;
                });
                return;
            }

            AddCommand(command, inputValidation);
            var reader = GetReceivedVectorsReader();
            _ = Task.Run(async () => 
            {
                await foreach (var vector in reader.ReadAllAsync(StopToken))
                foreach (var e in vector.Events)
                {
                    if (e.EventType != (byte)EventType.Command || !e.Data.StartsWith(command)) continue;
                    try
                    {
                        var cmd = _commandRunner.ParseCommand(e.Data);
                        if (cmd.Count <= 0 || cmd[0] != command || !inputValidation(cmd))
                            continue;

                        action(cmd);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(LogID.A, $"Error running command: {e.Data}", ex);
                    }
                }
            });
        }
        /// <remarks>all decisions must be added before running any subsystem, making decisions or getting vector descriptions i.e. add these early on</remarks>
        public void AddDecisions<T>(List<T> decisions) where T: LoopControlDecision => AddDecisions(decisions, _decisions);
        public void AddSafetyDecisions<T>(List<T> decisions) where T : LoopControlDecision => AddDecisions(decisions, _safetyDecisions);
        private void AddDecisions<T>(List<T> decisions, List<LoopControlDecision> targetList) where T : LoopControlDecision
        {
            //This just avoids the commands being reported as rejected for now, but the way to go about in the long run is to add detection of the executed commands by looking at the vectors.
            //Note that even then, the decisions are not reporting which commands they actually handled or ignored, specially as they are receiving all commands and then handle what applies to the decision.
            foreach (var (command, func) in decisions.SelectMany(DecisionExtensions.GetValidationCommands))
                AddCommand(command, func);

            targetList.AddRange(decisions);
        }

        /// <returns>an <see cref="Action"/> that can be used to unregister the command.</returns>
        public Action AddCommand(string name, Func<List<string>, bool> func) => _commandRunner.AddCommand(name, func);
        public void Execute(string command, bool isUserCommand = false) => HandleCommand(command, isUserCommand);
        public void AddSubsystem(ISubsystemWithVectorData subsystem) => _subsystems.Add(subsystem);
        public VectorDescription GetFullSystemVectorDescription() => GetExtendedVectorDescription().VectorDescription;
        public ExtendedVectorDescription GetExtendedVectorDescription() => _fullSystemFilterAndMath.Value;
        public IEnumerable<SensorSample> GetNodeInputs() => _subsystems.SelectMany(s => s.GetInputValues());
        public IEnumerable<SensorSample> GetGlobalInputs() => _subsystems.SelectMany(s => s.GetGlobalInputValues());
        public void MakeDecision(List<SensorSample> inputs, DateTime vectorTime, [NotNull] ref DataVector? vector, List<string> commands)
        {
            var extendedDesc = _fullSystemFilterAndMath.Value;
            DataVector.InitializeOrUpdateTime(ref vector, extendedDesc.VectorDescription.Length, vectorTime);
            extendedDesc.ApplyInputsTo(inputs, vector);
            MakeDecisionsAfterInputs(vector, commands, extendedDesc);
        }

        public void MakeDecisionUsingInputsFromNewVector(DataVector newVector, DataVector vector, List<string> events)
        {
            var extendedDesc = _fullSystemFilterAndMath.Value;
            DataVector.InitializeOrUpdateTime(ref vector, extendedDesc.VectorDescription.Length, newVector.Timestamp);
            extendedDesc.CopyInputsTo(newVector, vector);
            MakeDecisionsAfterInputs(vector, events, extendedDesc);
        }

        private void MakeDecisionsAfterInputs(DataVector vector, List<string> commands, ExtendedVectorDescription extendedDesc)
        {
            extendedDesc.MakeDecision(vector);
            var decisionsVector = new CA.LoopControlPluginBase.DataVector(vector.Timestamp, vector.Data);
            foreach (var decision in _decisions)
                decision.MakeDecision(decisionsVector, commands);
            foreach (var decision in _safetyDecisions)
                decision.MakeDecision(decisionsVector, commands);
        }

        public Task RunSubsystems() => RunSubsystems(StopToken);
        public Task RunSubsystems(CancellationToken token)
        {
            SendDeviceDetectionEvent();
            return Task.WhenAll(_subsystems.Select(s => s.Run(token)));
        }

        private void SendDeviceDetectionEvent()
        {
            var data = new SystemChangeNotificationData(GetCurrentNode().Name, Mapper.McuBoards.Select(ToBoardInfo).ToList()).ToJson();
            FireCustomEvent(data, DateTime.UtcNow, (byte)EventType.SystemChangeNotification);

            static SystemChangeNotificationData.BoardInfo ToBoardInfo(Board board) => new(board.PortName)
            {
                SerialNumber = board.SerialNumber,
                ProductType = board.ProductType,
                ProductSubType = board.SubProductType,
                McuFamily = board.McuFamily,
                SoftwareVersion = board.SoftwareVersion,
                CompileDate = board.SoftwareCompileDate,
                GitSha = board.GitSha,
                PcbVersion = board.PcbVersion,
                Calibration = board.Calibration,
                MappedBoardName = board.BoxName,
                UpdatedCalibration = board.UpdatedCalibration
            };
        }

        private ExtendedVectorDescription GetFullSystemFilterAndMath()
        {
            //note we regroup the inputs by node while keeping the node-subsystem order of inputs (returned by GetVectorDescriptionItems)
            var descItemsPerSubsystem = _subsystems.Select(s => s.GetVectorDescriptionItems()).ToList();
            var inputsPerNode = GetNodes()
                .Select(n => (node: n, inputs: descItemsPerSubsystem.SelectMany(s => s.GetNodeInputs(n)).ToList()))
                .Where(n => n.inputs.Count > 0)
                .Select(n => (n.node, (IReadOnlyList<VectorDescriptionItem>)n.inputs))
                .ToList();
            var globalInputs = descItemsPerSubsystem.SelectMany(s => s.GlobalInputs).ToList();
            OrderDecisionsBasedOnIOconf(_decisions);
            var decisions = _decisions.Concat(_safetyDecisions);
            SetConfigBasedOnIOconf(decisions);
            Logger.LogData(LogID.A, $"Decisions order: {string.Join(", ", decisions.Select(d => d.Name))}");
            var outputs = decisions.SelectMany(d => d.PluginFields.Select(f => new VectorDescriptionItem("double", f.Name, (DataTypeEnum)f.Type) { Upload = f.Upload })).ToList();
            var desc = new ExtendedVectorDescription(_ioconf, inputsPerNode, globalInputs, outputs);
            CA.LoopControlPluginBase.VectorDescription immutableVectorDesc = new(desc.VectorDescription._items.Select(i => i.Descriptor).ToArray());
            foreach (var decision in decisions)
                decision.Initialize(immutableVectorDesc);
            FullVectorDescriptionCreated?.Invoke(this, desc.VectorDescription);
            FullVectorIndexesCreated?.Invoke(
                this, desc.VectorDescription._items.Select((f, i) => (name: f.Descriptor, i)).ToDictionary(f => f.name, f => f.i));
            return desc;

            /// <summary>gets a comparer that can be used to order plugins based on the order listed in IO.conf</summary>
            /// <remarks>all decisions listed in the IO.conf come after the decisions not listed. The decisions that are not listed keep the order in which they were added</remarks>
            void OrderDecisionsBasedOnIOconf(List<LoopControlDecision> decisions)
            {
                //indexes are the original position at first and we later change the index of those found in IO.conf to decisions.Count + the conf order/index (so those in IO.conf come after non listed + in conf order).
                var decisionsIndexes = decisions.Select((decision, index) => (decision, index)).ToDictionary(tuple => tuple.decision.Name, tuple => (tuple.decision, tuple.index)); 
                var confDecisions = _ioconf.GetEntries<IOconfCode>();
                foreach (var conf in confDecisions)
                {
                    if (!decisionsIndexes.TryGetValue(conf.Name, out var decisionAndIndex)) 
                        throw new FormatException($"Decision listed in IO.conf (line {conf.LineNumber + 1}) was not found: '{conf.Row}'");
                    var decisionVersion = decisionAndIndex.decision.GetType().Assembly.GetName().Version ?? throw new FormatException($"Failed to retrieve assembly version for decision '{conf.Row}' (line {conf.LineNumber + 1})");
                    if (decisionVersion.Major != conf.Version.Major || decisionVersion.Minor != conf.Version.Minor || decisionVersion.Build != conf.Version.Build )
                        //the 3 digits the user sees/configures does not match the 4 digits the scxmltocode tool produces, so we compare the 3 digits explicitly above i.e. 1.0.2.0 vs. 1.0.2
                        throw new FormatException($"Decision listed in IO.conf (line {conf.LineNumber + 1}) did not match expected version: {conf.Version} - Actual: {decisionVersion} - '{conf.Row}'");
                    decisionsIndexes[conf.Name] = (decisionAndIndex.decision, decisions.Count + conf.Index); 
                }

                decisions.Sort((x, y) => decisionsIndexes[x.Name].index.CompareTo(decisionsIndexes[y.Name].index));
            }

            void SetConfigBasedOnIOconf(IEnumerable<LoopControlDecision> decisions)
            {
                var decisionsNames = new HashSet<string>(decisions.Select(d => d.Name));
                var configEntries = _ioconf.GetEntries<IOconfRow>();
                var unknownEntriesWithoutDecisions = configEntries.Where(e => e.IsUnknown && !decisionsNames.Contains(e.Type)).Select(r => r.Row).ToList();
                if (unknownEntriesWithoutDecisions.Count > 0)
                    throw new NotSupportedException($"Invalid config lines detected: {Environment.NewLine + string.Join(Environment.NewLine, unknownEntriesWithoutDecisions)}");
                var configEntriesLookup = configEntries.ToLookup(l => l.Type);
                foreach (var decision in decisions)
                    if (configEntriesLookup.Contains(decision.Name))
                        decision.SetConfig(new DecisionConfig(decision.Name, configEntriesLookup[decision.Name].ToDictionary(e => e.Name, e => string.Join(';', e.ToList().Skip(2)))));
            }
        }

        private IOconfNode GetCurrentNode() => GetNodes().Single(n => n.IsCurrentSystem);
        public List<IOconfNode> GetNodes()
        {
            var nodes = _ioconf.GetEntries<IOconfNode>().ToList();
            return nodes.Count > 0 ? nodes : [IOconfNode.GetSingleNode(_ioconf)];
        }

        public void OnNewVectorReceived(DataVector args)
        {
            foreach (var writer in _receivedVectorsWriters)
            {
                if (!writer.TryWrite(args) && IsRunning)
                    //at the time of writing the channel was unbounded, so it is not supposed to fail to add vectors to the channel
                    //note one case TryWrite may return false is when the channel is flagged as completed when we are stopping, thus the check for IsRunning above
                    throw new InvalidOperationException("Unexpected failure to write to the received vectors channel");
            }
        }

        public void FireAlert(string msg, DateTime timespan) => FireCustomEvent(msg, timespan, (byte)EventType.Alert);
        /// <summary>registers a custom event (low frequency, such like user commands and alerts that have a max firing rate)</summary>
        /// <remarks>preferably use values above 100 for eventType to avoid future collisions with built in event types</remarks>
        public void FireCustomEvent(string msg, DateTime timespan, byte eventType) 
        {
            EventFired?.Invoke(this, new EventFiredArgs(msg, eventType, timespan));
            _locallyFiredEvents.Writer.TryWrite(new(msg, eventType, timespan));
        }

        /// <summary>
        /// Returns a (new) list of dequeued events.
        /// </summary>
        /// <param name="max">The maximum number of events to dequeue.</param>
        /// <remarks>
        /// If events are created at a higher rate than the host processes some events will never been returned by this method.
        /// Subsystems normally throttle the error generation rate, but if some code path not doing that
        /// sees repeating errors that causes more than 200 events to be pending, the oldest events are dropped/skipped.
        /// </remarks>
        public List<EventFiredArgs>? DequeueEvents(int max = int.MaxValue)
        {
            List<EventFiredArgs>? list = null; // delayed initialization to avoid creating lists when there is no data.
            var reader = _locallyFiredEvents.Reader;
            for (int i = 0; i < max && reader.TryRead(out var e); i++)
                (list ??= []).Add(e);
            return list;
        }

        public bool Stop()
        {
            _exitCts.Cancel();
            return true;
        }

        private void LoopForever()
        {
            _start = DateTime.UtcNow;
            while (IsRunning)
            {
                try
                {
                    var cmd = GetCommand();
                    if (!string.IsNullOrWhiteSpace(cmd)) //cmd is null when GetCommand abort due to _running being false
                        HandleCommand(cmd, true);
                }
                catch (Exception ex)
                {
                    Logger.LogError(LogID.A, ex.ToString());
                }
            }

            Logger.LogInfo(LogID.A, "Exiting CommandHandler.LoopForever() " + DateTime.UtcNow.Subtract(_start));
        }

        private void HandleCommand(string cmdString, bool isUserCommand)
        {
            cmdString = cmdString.Trim();
            cmdString = Regex.Replace(cmdString, @"\s+", " "); // Merge multiple whitespace characters
            if (isUserCommand)
                UserCommandReceived?.Invoke(this, new(cmdString, EventType.Command, DateTime.UtcNow));
            if (_commandRunner.Run(cmdString, isUserCommand) && isUserCommand)
                OnUserCommandAccepted(cmdString);
        }

        private void OnUserCommandAccepted(string cmdString)
        {
            if (AcceptedCommands.LastOrDefault() != cmdString)
                AcceptedCommands.Add(cmdString);
            AcceptedCommandsIndex = AcceptedCommands.Count;
            FireCustomEvent(cmdString, DateTime.UtcNow, (byte)EventType.Command);
        }

        /// <returns>the text line <c>null</c> if we are no longer running (_running is false)</returns>
        private string? GetCommand()
        {
            inputCommand.Clear();
            var info = Console.ReadKey(true);
            while (info.Key != ConsoleKey.Enter)
            {
                if (!IsRunning)
                    return null; //no longer running, abort
                else if (info.Key == ConsoleKey.Escape)
                {
                    inputCommand.Clear();//clear input typed before the esc sequence
                    Console.WriteLine("You are about to stop the control program, type y if you want to continue");
                    var confirmedStop = Console.ReadKey().KeyChar == 'y';
                    var msg = confirmedStop ? "Stop sequence initiated" : "Stop sequence aborted";
                    Console.WriteLine(msg); //ensure there is console output for this interactive local action regardless of the logger
                    Logger.LogData(LogID.A, msg); //no need to write the line to the screen again + no need to show it in remote logging, as confirmed stops show as the "escape" command.
                    if (confirmedStop)
                        return "escape";
                }
                else if (info.Key == ConsoleKey.Backspace)
                {
                    Console.Write("\b \b"); // https://stackoverflow.com/a/53976873/66372
                    if (inputCommand.Length > 0)
                        inputCommand.Remove(inputCommand.Length - 1, 1);
                }
                else if (info.Key == ConsoleKey.UpArrow)
                    RestorePreviousAcceptedCommand();
                else if (info.Key == ConsoleKey.DownArrow)
                    RestoreNextAcceptedCommand();
                else
                {
                    Console.Write(info.KeyChar);
                    inputCommand.Append(info.KeyChar);
                }
                info = Console.ReadKey(true);
            }
            
            Console.WriteLine(string.Empty); //write the newline as the user pressed enter
            return inputCommand.ToString();
        }

        private void RestoreNextAcceptedCommand()
        {
            if (AcceptedCommandsIndex >= AcceptedCommands.Count - 1)
                return;

            AcceptedCommandsIndex++;
            ReplaceInputCommand(AcceptedCommands[AcceptedCommandsIndex]);
        }

        private void RestorePreviousAcceptedCommand()
        {
            if (AcceptedCommandsIndex > 0)

            AcceptedCommandsIndex--;
            ReplaceInputCommand(AcceptedCommands[AcceptedCommandsIndex]);
        }

        private void ReplaceInputCommand(string cmd)
        {
            inputCommand.Clear();
            inputCommand.Append(cmd);
            Console.Write("\r" + new string(' ', Console.WindowWidth) + "\r"); // clear line first https://stackoverflow.com/a/14083947/66372
            Console.Write("\r" + cmd);
        }

        private bool HelpMenu(List<string> args)
        {
            Logger.LogInfo(LogID.A, "-------------------------------------");
            Logger.LogInfo(LogID.A, "Commands: ");
            Logger.LogInfo(LogID.A, "Esc                       - press Esc key to shut down");
            Logger.LogInfo(LogID.A, "help                      - print the full list of available commands");
            Logger.LogInfo(LogID.A, "up                        - print how long the service has been running");
            Logger.LogInfo(LogID.A, "version                   - print the software version and hardware of this system");
            return true;
        }

        public void Uptime(List<string> _) => 
            Logger.LogInfo(LogID.A, $"{DateTime.UtcNow.Subtract(_start)}");

        public void GetVersion(List<string> _)
        {
            var connInfo = _ioconf.GetConnectionInfo();
            Logger.LogInfo(LogID.A, 
$@"{RpiVersion.GetSoftware()}
{RpiVersion.GetHardware()}
{connInfo.LoopName} - {connInfo.Email}
{(_mapper != null ? string.Join(Environment.NewLine, Mapper.McuBoards.Select(x => x.ToString())) : "")}");
        }

        public void Dispose()
        { // class is sealed without unmanaged resources, no need for the full disposable pattern.
            _exitCts.Cancel();
        }
    }
}
