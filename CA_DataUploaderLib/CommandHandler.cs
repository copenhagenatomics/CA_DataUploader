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

namespace CA_DataUploaderLib
{
    public sealed class CommandHandler : IDisposable
    {
        private readonly SerialNumberMapper _mapper;
        private readonly ICommandRunner _commandRunner;
        private DateTime _start = DateTime.Now;
        private readonly StringBuilder inputCommand = new();
        private readonly List<string> AcceptedCommands = new();
        private readonly List<ISubsystemWithVectorData> _subsystems = new();
        private int AcceptedCommandsIndex = -1;
        private readonly List<LoopControlDecision> _decisions = new();
        private readonly Lazy<ExtendedVectorDescription> _fullsystemFilterAndMath;
        private readonly CancellationTokenSource _exitCts = new();
        private readonly TaskCompletionSource _runningTaskTcs = new();

        public event EventHandler<NewVectorReceivedArgs> NewVectorReceived;
        public event EventHandler<EventFiredArgs> EventFired;
        public bool IsRunning => !_exitCts.IsCancellationRequested;
        public CancellationToken StopToken => _exitCts.Token;
        public Task RunningTask => _runningTaskTcs.Task;

        public CommandHandler(SerialNumberMapper mapper = null, ICommandRunner runner = null)
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

        internal void AddDecisions(IEnumerable<LoopControlDecision> decisions) => _decisions.AddRange(decisions);
        /// <returns>an <see cref="Action"/> that can be used to unregister the command.</returns>
        public Action AddCommand(string name, Func<List<string>, bool> func) => _commandRunner.AddCommand(name, func);
        public void Execute(string command, bool isUserCommand = false) => HandleCommand(command, isUserCommand);
        public void AddSubsystem(ISubsystemWithVectorData subsystem) => _subsystems.Add(subsystem);
        public VectorDescription GetFullSystemVectorDescription() => GetExtendedVectorDescription().VectorDescription;
        public ExtendedVectorDescription GetExtendedVectorDescription() => _fullsystemFilterAndMath.Value;
        /// <remarks>This method is only aimed at single host scenarios where a single system has all the inputs</remarks>
        public DataVector GetFullSystemVectorValues(DataVector lastVector, List<string> events) 
        {
            var time = DateTime.UtcNow;
            var vector = MakeDecision(GetNodeInputs().ToList(), time, lastVector, events);
            OnNewVectorReceived(ToNewVectorReceivedArgs(vector, _fullsystemFilterAndMath.Value.VectorDescription._items));
            return vector;
        }
        public IEnumerable<SensorSample> GetNodeInputs() => _subsystems.SelectMany(s => s.GetInputValues());
        public DataVector MakeDecision(List<SensorSample> inputs, DateTime vectorTime, DataVector lastVector, List<string> events)
        {
            var filterAndMath = _fullsystemFilterAndMath.Value;
            var vector = filterAndMath.Apply(inputs, vectorTime);
            CopyStatesFieldsToNewVector(lastVector, vector, filterAndMath.VectorDescription);
            var inputVectorReceivedArgs = ToNewVectorReceivedArgs(vector, filterAndMath.VectorDescription._items);
            var outputs = _subsystems.SelectMany(s => s.GetDecisionOutputs(inputVectorReceivedArgs));
            filterAndMath.AddOutputs(vector, outputs);
            var decisionsVector = new CA.LoopControlPluginBase.DataVector(vector.timestamp, vector.data);
            foreach (var decision in _decisions)
                decision.MakeDecision(decisionsVector, events);
            return vector;
        }

        static void CopyStatesFieldsToNewVector(DataVector lastVector, DataVector newVector, VectorDescription desc)
        {
            var oldData = lastVector.data;
            var newData = newVector.data;
            for (int i = 0; i < desc._items.Count; i++)
            {
                if (desc._items[i].Descriptor.StartsWith("state_"))
                    newData[i] = oldData[i];
            }
        }

        //note: the below is only needed to support the GetDecisionOutputs style, if all moves to the LoopControlDecision contract this can be removed
        static NewVectorReceivedArgs ToNewVectorReceivedArgs(DataVector vector, List<VectorDescriptionItem> fields)
        {
            var data = vector.data;
            var vectorDic = new Dictionary<string, double>();
            for (int i = 0; i < vector.Count(); i++)
                vectorDic.Add(fields[i].Descriptor, data[i]);

            vectorDic.Add("vectortime", vector.timestamp.ToVectorDouble());
            return new NewVectorReceivedArgs(vectorDic);
        }

        public void ResumeState(List<SensorSample> fullVector)
        {
            var args = new NewVectorReceivedArgs(fullVector.ToDictionary(v => v.Name, v => v.Value));
            foreach (var subsystem in _subsystems)
                subsystem.ResumeState(args);
        }

        public Task RunSubsystems()
        {
            SendDeviceDetectionEvent();
            return Task.WhenAll(_subsystems.Select(s => s.Run(CancellationToken.None)));
        }

        private void SendDeviceDetectionEvent()
        {
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
            var outputs = descItemsPerSubsystem
                .SelectMany(s => s.Outputs)
                .Concat(_decisions.SelectMany(d => d.PluginFields.Select(f => new VectorDescriptionItem("double", f, DataTypeEnum.State))))
                .ToList();
            return new ExtendedVectorDescription(inputsPerNode, outputs, RpiVersion.GetHardware(), RpiVersion.GetSoftware());
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
        private string GetCommand()
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
{string.Join(Environment.NewLine, _mapper.McuBoards.Select(x => x.ToString()))}");
            return true;
        }

        public void Dispose()
        { // class is sealed without unmanaged resources, no need for the full disposable pattern.
            _exitCts.Cancel();
        }
    }
}
