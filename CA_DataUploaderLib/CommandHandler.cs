﻿#nullable enable
using CA.LoopControlPluginBase;
using CA_DataUploaderLib.Helpers;
using CA_DataUploaderLib.IOconf;
using CA_DataUploaderLib.Extensions;
using Humanizer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics.CodeAnalysis;

namespace CA_DataUploaderLib
{
    public sealed class CommandHandler : IDisposable
    {
        private readonly SerialNumberMapper? _mapper;
        private readonly ICommandRunner _commandRunner;
        private DateTime _start = DateTime.Now;
        private readonly StringBuilder inputCommand = new();
        private readonly List<string> AcceptedCommands = new();
        private readonly List<ISubsystemWithVectorData> _subsystems = new();
        private int AcceptedCommandsIndex = -1;
        private readonly List<LoopControlDecision> _decisions = new();
        private readonly List<LoopControlDecision> _safetydecisions = new();
        private readonly Lazy<ExtendedVectorDescription> _fullsystemFilterAndMath;
        private readonly CancellationTokenSource _exitCts = new();
        private readonly TaskCompletionSource _runningTaskTcs = new();

        public event EventHandler<NewVectorReceivedArgs>? NewVectorReceived;
        public event EventHandler<EventFiredArgs>? EventFired;
        public bool IsRunning => !_exitCts.IsCancellationRequested;
        public CancellationToken StopToken => _exitCts.Token;
        public Task RunningTask => _runningTaskTcs.Task;

        public CommandHandler(SerialNumberMapper? mapper = null, ICommandRunner? runner = null)
        {
            _exitCts.Token.Register(() => _runningTaskTcs.TrySetCanceled());
            _commandRunner = runner ?? new DefaultCommandRunner();
            _mapper = mapper;
            _fullsystemFilterAndMath = new Lazy<ExtendedVectorDescription>(GetFullSystemFilterAndMath);
            new Thread(() => this.LoopForever()).Start();
            AddCommand("escape", Stop);
            AddCommand("help", HelpMenu);
            AddCommand("up", Uptime);
            AddCommand("version", GetVersion);
        }

        /// <remarks>all decisions must be added before running any subsystem, making decisions or getting vector descriptions i.e. add these early on</remarks>
        public void AddDecisions<T>(List<T> decisions) where T: LoopControlDecision => AddDecisions(decisions, _decisions);
        public void AddSafetyDecisions<T>(List<T> decisions) where T : LoopControlDecision => AddDecisions(decisions, _safetydecisions);
        private void AddDecisions<T>(List<T> decisions, List<LoopControlDecision> targetList) where T : LoopControlDecision
        {
            foreach (var e in decisions.SelectMany(d => d.HandledEvents))
            {
                //This just avoids the commands being reported as rejected for now, but the way to go about in the long run is to add detection of the executed commands by looking at the vectors.
                //Note that even then, the decisions are not reporting which commands they actually handled or ignore, specially as they are receiving all commands and then handle what applies to the decision.
                var firstWhitespace = e.IndexOf(' ');
                var firstWordInEvent = firstWhitespace != -1 ? e[..firstWhitespace] : e;
                AddCommand(firstWordInEvent, _ => true);
            }

            targetList.AddRange(decisions);
        }

        /// <returns>an <see cref="Action"/> that can be used to unregister the command.</returns>
        public Action AddCommand(string name, Func<List<string>, bool> func) => _commandRunner.AddCommand(name, func);
        public void Execute(string command, bool isUserCommand = false) => HandleCommand(command, isUserCommand);
        public void AddSubsystem(ISubsystemWithVectorData subsystem) => _subsystems.Add(subsystem);
        public VectorDescription GetFullSystemVectorDescription() => GetExtendedVectorDescription().VectorDescription;
        public ExtendedVectorDescription GetExtendedVectorDescription() => _fullsystemFilterAndMath.Value;
        /// <remarks>This method is only aimed at single host scenarios where a single system has all the inputs</remarks>
        public void GetFullSystemVectorValues(ref DataVector? vector, List<string> events) 
        {
            var time = DateTime.UtcNow;
            MakeDecision(GetNodeInputs().ToList(), time, ref vector, events);
            OnNewVectorReceived(ToNewVectorReceivedArgs(vector, _fullsystemFilterAndMath.Value.VectorDescription._items));
        }
        public IEnumerable<SensorSample> GetNodeInputs() => _subsystems.SelectMany(s => s.GetInputValues());
        public void MakeDecision(List<SensorSample> inputs, DateTime vectorTime, [NotNull]ref DataVector? vector, List<string> events)
        {
            var extendedDesc = _fullsystemFilterAndMath.Value;
            DataVector.InitializeOrUpdateTime(ref vector, extendedDesc.VectorDescription.Length, vectorTime);
            extendedDesc.Apply(inputs, vector);
            var decisionsVector = new CA.LoopControlPluginBase.DataVector(vector.Timestamp, vector.Data);
            foreach (var decision in _decisions)
                decision.MakeDecision(decisionsVector, events);
            foreach (var decision in _safetydecisions)
                decision.MakeDecision(decisionsVector, events);
        }

        //note: the below is only needed to support the GetDecisionOutputs style, if all moves to the LoopControlDecision contract this can be removed
        static NewVectorReceivedArgs ToNewVectorReceivedArgs(DataVector vector, List<VectorDescriptionItem> fields)
        {
            var data = vector.Data;
            var vectorDic = new Dictionary<string, double>();
            for (int i = 0; i < vector.Count(); i++)
                vectorDic.Add(fields[i].Descriptor, data[i]);

            vectorDic.Add("vectortime", vector.Timestamp.ToVectorDouble());
            return new NewVectorReceivedArgs(vectorDic);
        }

        public Task RunSubsystems()
        {
            SendDeviceDetectionEvent();
            return Task.WhenAll(_subsystems.Select(s => s.Run(CancellationToken.None)));
        }

        private void SendDeviceDetectionEvent()
        {
            if (_mapper == null) 
            {
                CALog.LogErrorAndConsoleLn(LogID.A, "can't send detection event due to CommandHandler being invoked with a null SerialNumberMapper");
                return;
            }

            var data = new SystemChangeNotificationData() { NodeName = GetCurrentNode().Name, Boards = _mapper.McuBoards.Select(ToBoardInfo).ToList() }.ToJson();
            FireCustomEvent(data, DateTime.UtcNow, (byte)EventType.SystemChangeNotification);

            static SystemChangeNotificationData.BoardInfo ToBoardInfo(MCUBoard board) => new()
            {
                SerialNumber = board.serialNumber,
                ProductType = board.productType,
                ProductSubType = board.subProductType,
                McuFamily = board.mcuFamily,
                SoftwareVersion = board.softwareVersion,
                CompileDate = board.softwareCompileDate,
                GitSha = board.GitSha,
                PcbVersion = board.pcbVersion,
                Calibration = board.Calibration,
                MappedBoardName = board.BoxName,
                Port = board.PortName,
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
            var decisions = _decisions.Concat(_safetydecisions);
            var decisionsNames = new HashSet<string>(_decisions.Select(d => d.Name));
            var configEntries = IOconfFile.GetEntries<IOconfRow>();
            var unknownEntriesWithoutDecisions = configEntries.Where(e => e.IsUnknown && !decisionsNames.Contains(e.Type)).Select(r => r.Row).ToList();
            if (unknownEntriesWithoutDecisions.Count > 0)
                throw new NotSupportedException($"invalid config lines detected: {Environment.NewLine + string.Join(Environment.NewLine, unknownEntriesWithoutDecisions)}");
            var configEntriesLookup = configEntries.ToLookup(l => l.Type);
            foreach (var decision in decisions)
                if (configEntriesLookup.Contains(decision.Name))
                    decision.SetConfig(new DecisionConfig(decision.Name, configEntriesLookup[decision.Name].ToDictionary(e => e.Name, e => string.Join(';', e.ToList().Skip(2)))));
            var outputs = decisions.SelectMany(d => d.PluginFields.Select(f => new VectorDescriptionItem("double", f.Name, (DataTypeEnum)f.Type) { Upload = f.Upload })).ToList();
            var desc = new ExtendedVectorDescription(inputsPerNode, outputs, RpiVersion.GetHardware(), RpiVersion.GetSoftware());
            foreach (var decision in decisions)
                decision.Initialize(new(desc.VectorDescription._items.Select(i => i.Descriptor).ToArray()));
            return desc;
        }

        private static IOconfNode GetCurrentNode() => GetNodes().Single(n => n.IsCurrentSystem);
        private static List<IOconfNode> GetNodes()
        {
            var nodes = IOconfFile.GetEntries<IOconfNode>().ToList();
            return nodes.Count > 0 ? nodes : new() { IOconfNode.SingleNode };
        }

        public bool AssertArgs(List<string> args, int minimumLen)
        {
            if (args.Count < minimumLen)
            {
                CALog.LogInfoAndConsoleLn(LogID.A, "Too few arguments for this command");
                Execute("help");
                return false;
            }

            return true;
        }

        public void OnNewVectorReceived(IEnumerable<SensorSample> vector) =>
            NewVectorReceived?.Invoke(this, new NewVectorReceivedArgs(vector.ToDictionary(v => v.Name, v => v.Value)));
        public void OnNewVectorReceived(NewVectorReceivedArgs args) => NewVectorReceived?.Invoke(this, args);

        public void FireAlert(string msg, DateTime timespan)
        {
            CALog.LogErrorAndConsoleLn(LogID.A, msg);
            FireCustomEvent(msg, timespan, (byte)EventType.Alert);
        }

        /// <summary>registers a custom event (low frequency, such like user commands and alerts that have a max firing rate)</summary>
        /// <remarks>preferably use values above 100 for eventType to avoid future collisions with built in event types</remarks>
        public void FireCustomEvent(string msg, DateTime timespan, byte eventType) => EventFired?.Invoke(this, new EventFiredArgs(msg, eventType, timespan));
        private bool Stop(List<string> args)
        {
            _exitCts.Cancel();
            return true;
        }

        private void LoopForever()
        {
            _start = DateTime.Now;
            while (IsRunning)
            {
                try
                {
                    var cmd = GetCommand();
                    if (cmd != null) //cmd is null when GetCommand abort due to _running being false
                        HandleCommand(cmd, true);
                }
                catch (Exception ex)
                {
                    CALog.LogErrorAndConsoleLn(LogID.A, ex.ToString());
                }
            }

            CALog.LogInfoAndConsoleLn(LogID.A, "Exiting CommandHandler.LoopForever() " + DateTime.Now.Subtract(_start).Humanize(5));
        }

        private void HandleCommand(string cmdString, bool isUserCommand)
        {
            cmdString = cmdString.Trim();
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
                    CALog.LogData(LogID.A, msg); //no need to write the line to the screen again + no need to show it in remote logging, as confirmed stops show as the "escape" command.
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

        public static int GetCmdParam(List<string> cmd, int index, int defaultValue) =>
            cmd.Count > index && int.TryParse(cmd[index], out int value) ? value : defaultValue;

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
            CALog.LogInfoAndConsoleLn(LogID.A, "-------------------------------------");
            CALog.LogInfoAndConsoleLn(LogID.A, "Commands: ");
            CALog.LogInfoAndConsoleLn(LogID.A, "Esc                       - press Esc key to shut down");
            CALog.LogInfoAndConsoleLn(LogID.A, "help                      - print the full list of available commands");
            CALog.LogInfoAndConsoleLn(LogID.A, "up                        - print how long the service has been running");
            CALog.LogInfoAndConsoleLn(LogID.A, "version                   - print the software version and hardware of this system");
            return true;
        }

        public bool Uptime(List<string> args)
        {
            CALog.LogInfoAndConsoleLn(LogID.A, $"{GetCurrentNode().Name} - {DateTime.Now.Subtract(_start).Humanize(5)}");
            return true;
        }

        public bool GetVersion(List<string> args)
        {
            var connInfo = IOconfFile.GetConnectionInfo();
            CALog.LogInfoAndConsoleLn(LogID.A, 
$@"{GetCurrentNode().Name}
{RpiVersion.GetSoftware()}
{RpiVersion.GetHardware()}
{connInfo.LoopName} - {connInfo.email}
{(_mapper != null ? string.Join(Environment.NewLine, _mapper.McuBoards.Select(x => x.ToString())) : "")}");
            return true;
        }

        public void Dispose()
        { // class is sealed without unmanaged resources, no need for the full disposable pattern.
            _exitCts.Cancel();
        }
    }
}
