using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Text;
using CA_DataUploaderLib.IOconf;
using Humanizer;
using System.Diagnostics;
using System.Collections;
using CA.LoopControlPluginBase;
using System.Threading.Tasks;

namespace CA_DataUploaderLib
{
    public class BaseSensorBox : IDisposable, ISubsystemWithVectorData
    {
        public string Title { get; protected set; }
        /// <summary>runs when the subsystem is about to stop running, but before all boards are closed</summary>
        /// <remarks>some boards might be closed, specially if the system is stopping due to losing connection to one of the boards</remarks>
        public event EventHandler Stopping;
        private readonly CommandHandler _cmd;
        protected readonly List<SensorSample> _values;
        protected readonly List<SensorSample> _localValues;
        private readonly (IOconfMap map, SensorSample[] values)[] _boards;
        protected readonly AllBoardsState _boardsState;
        private readonly string commandHelp;
        private readonly CancellationTokenSource _boardLoopsStopTokenSource = new CancellationTokenSource();
        private readonly Dictionary<MCUBoard, SensorSample[]> _boardSamplesLookup = new Dictionary<MCUBoard, SensorSample[]>();
        private readonly string mainSubsystem;

        public BaseSensorBox(
            CommandHandler cmd, string commandName, string commandArgsHelp, string commandDescription, IEnumerable<IOconfInput> values)
        { 
            Title = commandName;
            _cmd = cmd;
            _values = values.Select(x => new SensorSample(x)).ToList();
            _localValues = _values.Where(x => x.Input.Map.IsLocalBoard).ToList();
            if (!_values.Any())
                return;  // no data

            if (cmd != null)
            {
                mainSubsystem = commandName.ToLower();
                commandHelp = $"{mainSubsystem + " " + commandArgsHelp,-26}- {commandDescription}";
                SubscribeCommandsToSubsystems(cmd, mainSubsystem, _values);
                cmd.AddCommand("help", HelpMenu);
                cmd.AddCommand("escape", Stop);
                cmd.AddSubsystem(this);
            }

            var allBoards = _values.Where(x => !x.Input.Skip).GroupBy(x => x.Input.Map).Select(g => (map: g.Key, values: g.ToArray())).ToArray();
            _boards = allBoards.Where(b => b.map.IsLocalBoard).ToArray();
            _boardsState = new AllBoardsState(_boards.Select(b => b.map));
        }

        public Task Run(CancellationToken token) => RunBoardReadLoops(_boards, token);
        private void SubscribeCommandsToSubsystems(CommandHandler cmd, string mainSubsystem, List<SensorSample> values)
        {
            cmd.AddCommand(mainSubsystem, ShowQueue);
            var subsystemOverrides = values.Select(v => v.Input.SubsystemOverride).Where(v => v != default).Distinct();
            foreach (var subsystem in subsystemOverrides)
            {
                if (subsystem == mainSubsystem) continue;
                cmd.AddCommand(subsystem, ShowQueue);
            }
        }

        public IEnumerable<SensorSample> GetInputValues() => _localValues
            .Select(s => s.Clone())
            .Concat(_boardsState.Select(b => new SensorSample(b.sensorName, (int)b.State)));

        public IEnumerable<SensorSample> GetDecisionOutputs(NewVectorReceivedArgs inputVectorReceivedArgs) => Enumerable.Empty<SensorSample>();
        public virtual SubsystemDescriptionItems GetVectorDescriptionItems()
        {
            var nodes = _values.GroupBy(v => v.Input.Map.DistributedNode);
            var valuesByNode = nodes.Select(n => (n.Key, GetNodeDescItems(n))).ToList();
            return new SubsystemDescriptionItems(valuesByNode, new());

            static List<VectorDescriptionItem> GetNodeDescItems(IEnumerable<SensorSample> values) =>
                values.Select(v => new VectorDescriptionItem("double", v.Input.Name, DataTypeEnum.Input))
                 .Concat(GetBoards(values).Select(b => new VectorDescriptionItem("double", b.BoxName + "_state", DataTypeEnum.State)))
                 .ToList();
            static IEnumerable<IOconfMap> GetBoards(IEnumerable<SensorSample> n) =>
                n.Where(v => !v.Input.Skip).GroupBy(v => v.Input.Map).Select(b => b.Key);
        }

        protected bool ShowQueue(List<string> args)
        {
            var subsystem = args[0].ToLower();
            var isMainCommand = subsystem == mainSubsystem;
            StringBuilder sb;
            if (isMainCommand)
            {
                sb = new StringBuilder($"NAME      {GetAvgLoopTime(),4:N0}           ");
                sb.AppendLine();
            }
            else
                sb = new StringBuilder();
            foreach (var t in _values)
            {
                string subsystemOverride = t.Input.SubsystemOverride;
                var matchesSubsystem = (isMainCommand && subsystemOverride == default) || subsystemOverride == subsystem;
                if (matchesSubsystem)
                    sb.AppendLine($"{t.Input.Name,-22}={t.Value,9:N2}");
            }

            CALog.LogInfoAndConsoleLn(LogID.A, sb.ToString());
            return true;
        }

        private double GetAvgLoopTime() => _values.Average(x => x.ReadSensor_LoopTime);

        private async Task RunBoardReadLoops((IOconfMap map, SensorSample[] values)[] boards, CancellationToken token)
        {
            DateTime start = DateTime.Now;
            try
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, _boardLoopsStopTokenSource.Token);
                var readLoops = StartReadLoops(boards, linkedCts.Token);
                await Task.WhenAll(readLoops);
                CALog.LogInfoAndConsoleLn(LogID.A, $"Exiting {Title}.RunBoardReadLoops() " + DateTime.Now.Subtract(start).Humanize(5));
            }
            catch (Exception ex)
            {
                CALog.LogErrorAndConsoleLn(LogID.A, ex.ToString());
            }

            Stopping?.Invoke(this, EventArgs.Empty);
            foreach (var (map, _) in boards)
            {
                try
                {
                    using var closeTimeoutToken = new CancellationTokenSource(5000);
                    map.Board?.SafeClose(closeTimeoutToken.Token);
                    _boardsState.SetDisconnectedState(map);
                }
                catch(Exception ex)
                {
                    LogError(map, "error closing the connection to the board", ex);
                }
            }
        }

        protected virtual List<Task> StartReadLoops((IOconfMap map, SensorSample[] values)[] boards, CancellationToken token)
        {
            var missingBoards = boards.Where(h => h.map.Board == null).Select(b => b.map.BoxName).Distinct().ToList();
            if (missingBoards.Count > 0)
                CALog.LogErrorAndConsoleLn(LogID.A, $"{Title} - missing boards detected {string.Join(",", missingBoards)}. Related sensors/actuators are disabled.");

            return boards
                .Where(b => b.map.Board != null) //we ignore the missing boards for now as we don't have auto reconnect logic yet for boards not detected during system start.
                .Select(b => BoardLoop(b.map.Board, b.values, token))
                .ToList();
        }

        private async Task BoardLoop(MCUBoard board, SensorSample[] targetSamples, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var stillConnected = await SafeReadSensors(board, targetSamples, token);
                    if (!stillConnected || CheckFails(board, targetSamples))
                        await ReconnectBoard(board, token);
                }
                catch (TaskCanceledException ex)
                { //if the token is canceled we are about to exit the loop so we do nothing. Otherwise we consider it like any other exception and log it.
                    if (!token.IsCancellationRequested)
                    {
                        _boardsState.SetReadSensorsExceptionState(board);
                        LogError(board, "unexpected error on board read loop", ex);
                    }
                }
                catch (Exception ex)
                { // we expect most errors to be handled within SafeReadSensors and in the SafeReopen of the ReconnectBoard,
                  // so seeing this in the log is most likely a bug handling some error case.
                    _boardsState.SetReadSensorsExceptionState(board);
                    LogError(board, "unexpected error on board read loop", ex);
                }
            }
        }

        ///<returns><c>false</c> if a board disconnect was detected</returns>
        private async Task<bool> SafeReadSensors(MCUBoard board, SensorSample[] targetSamples, CancellationToken token)
        { //we use this to prevent read exceptions from interfering with failure checks and reconnects
            try
            {
                return await ReadSensors(board, targetSamples, token);
            }
            catch (TaskCanceledException ex)
            { 
                if (token.IsCancellationRequested)
                    throw; // if the token is cancelled we bubble up the cancel so the caller can abort.
                _boardsState.SetReadSensorsExceptionState(board);
                LogError(board, "error reading sensor data", ex);
                return true; //ReadSensor normally should return false if the board is detected as disconnected, so we say the board is still connected here since the caller will still do stale values detection
            }
            catch (Exception ex)
            { // seeing this is the log is not unexpected in cases where we have trouble communicating to a board.
                _boardsState.SetReadSensorsExceptionState(board);
                LogError(board, "error reading sensor data", ex);
                return true; //ReadSensor normally should return false if the board is detected as disconnected, so we say the board is still connected here since the caller will still do stale values detection
            }
        }

        ///<returns><c>false</c> if a board disconnect was detected</returns>
        private async Task<bool> ReadSensors(MCUBoard board, SensorSample[] targetSamples, CancellationToken token)
        {
            var timeSinceLastValidRead = Stopwatch.StartNew();
            // we need to allow some extra time to avoid too aggressive reporting of boards not giving data, no particular reason for it being 50%.
            var msBetweenReads = (int)Math.Ceiling(board.ConfigSettings.MillisecondsBetweenReads * 1.5); 
            //We set the state early if we detect no data is being returned or if we received values,
            //but we only set ReturningNonValues if it has passed msBetweenReads since the last valid read
            while (true) // we only stop reading if a disconnect or timeout is detected
            {
                var (stillConnected, line) = await TryReadLineWithStallDetection(board, msBetweenReads, token);
                if (!stillConnected)
                    return false;  //board disconnect detected, let caller know 
                if (line == default) 
                    return true; //timed out reading from the board, TryReadLineWithStallDetection already updated the state after the first msBetweenReads / still considered connected
                try
                {
                    var numbers = TryParseAsDoubleList(board, line);
                    if (numbers != null)
                    {
                        ProcessLine(numbers, board, targetSamples);
                        timeSinceLastValidRead.Restart();
                        _boardsState.SetState(board, ConnectionState.ReceivingValues);
                    }
                    else if (!board.ConfigSettings.Parser.IsExpectedNonValuesLine(line))// mostly responses to commands or headers on reconnects.
                    {
                        LogInfo(board, $"unexpected board response {line.Replace("\r", "\\r")}"); // we avoid \r as it makes the output hard to read
                        if (timeSinceLastValidRead.ElapsedMilliseconds > msBetweenReads)
                            _boardsState.SetState(board, ConnectionState.ReturningNonValues);
                    }
                }
                catch (Exception ex)
                { //usually a parsing errors on non value data, we log it and consider it as such i.e. we set ReturningNonValues if we have not had a valid read in msBetweenReads
                    LogError(board, $"failed handling board response {line.Replace("\r", "\\r")}", ex); // we avoid \r as it makes the output hard to read
                    if (timeSinceLastValidRead.ElapsedMilliseconds > msBetweenReads)
                        _boardsState.SetState(board, ConnectionState.ReturningNonValues);
                }
            }
        }

        ///<returns>(<c>false</c> if the board was explicitely detected as disconnected, the line, or null/default string if it exceeded the MCUBoard.ReadTimeout).</returns>
        ///<remarks>
        ///Notifies in _localBoardsState (which is reported to the next vector) if there is no data available within <param cref="msBetweenReads" /> 
        ///and when that happens it waits up to MCUBoard.ReadTimeout for the board to return data.
        ///Both in the above case and when the MCUBoard.ReadTimeout is exceeded a message is written to the log (but not the console to reduce operational noise),
        ///specially as it can now be observed on the graphs if board these events are happening + CheckFailure & reconnects will display relevant messages if appropiate.
        ///</remarks>
        private async Task<(bool, string)> TryReadLineWithStallDetection(MCUBoard board, int msBetweenReads, CancellationToken token)
        {
            var readLineTask = board.SafeReadLine(token);
            var noDataAvailableTask = Task.Delay(msBetweenReads, token); 
            if (await Task.WhenAny(readLineTask, noDataAvailableTask) == noDataAvailableTask)
            {
                LogData(board, "no data available");
                _boardsState.SetState(board, ConnectionState.NoDataAvailable); //report the state early before waiting up to 2 seconds for the data (readLineTask)
            }

            try
            {
                return (true, await readLineTask); // waits up to 2 seconds for the read to complete, while we are here the state keeps being no data.
            }
            catch (ObjectDisposedException)
            {
                LogData(board, "detected closed connection");
                return (false, default);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                //typically readline reports ObjectDisposedException once when disconnecting a temp hub and later calls fail with "ArgumentOutOfRangeException: Non-negative number required."
                //calling code is expected to handle the disconnected board returned by ObjectDisposedException so this is logged and displayed as an error.
                //more testing is needed with switchboards, as potentially a write could get the ObjectDisposeException which might end up hitting this catch statement.
                LogError(board, "detected closed connection", ex);
                return (false, default);
            }
            catch (TimeoutException)
            {
                LogData(board, "timed out reading data");
                return (true, default);
            }
        }

        /// <returns>the list of doubles, otherwise <c>null</c></returns>
        protected virtual List<double> TryParseAsDoubleList(MCUBoard board, string line) => 
            board.ConfigSettings.Parser.TryParseAsDoubleList(line);

        private bool CheckFails(MCUBoard board, SensorSample[] values)
        {
            bool hasStaleValues = false;
            foreach (var item in values)
            {
                var msSinceLastRead = DateTime.UtcNow.Subtract(item.TimeStamp).TotalMilliseconds;
                if (msSinceLastRead <= item.Input.Map.Board.ConfigSettings.MaxMillisecondsWithoutNewValues)
                    continue;
                hasStaleValues = true; 
                LogInfo(board, $"stale sensor detected: {item.Input.Name}. {msSinceLastRead} milliseconds since last read");
            }

            return hasStaleValues;
        }

        private async Task ReconnectBoard(MCUBoard board, CancellationToken token)
        {
            _boardsState.SetAttemptingReconnectState(board);
            LogInfo(board, "attempting to reconnect");
            var lostSensorAttempts = 100;
            var delayBetweenAttempts = TimeSpan.FromSeconds(board.ConfigSettings.SecondsBetweenReopens);
            while (!(await board.SafeReopen(token)))
            {
                _boardsState.SetDisconnectedState(board);
                if (ExactSensorAttemptsCheck(ref lostSensorAttempts)) 
                { // we run this once when there has been 100 attempts
                    LogError(board, "reconnect limit exceeded, reducing reconnect frequency to 15 minutes");
                    delayBetweenAttempts = TimeSpan.FromMinutes(15); // 4 times x hour = 96 times x day
                }

                await Task.Delay(delayBetweenAttempts, token);
            }

            _boardsState.SetConnectedState(board);
            LogInfo(board, "board reconnection succeeded");
        }

        private static bool ExactSensorAttemptsCheck(ref int lostSensorAttempts)
        {
             if (lostSensorAttempts > 0)
                lostSensorAttempts--;
            else if (lostSensorAttempts == 0) 
            {
                lostSensorAttempts--;
                return true;
            }
            return false;
        }

        protected bool Stop(List<string> args)
        {
            _boardLoopsStopTokenSource.Cancel();
            return true;
        }

        public virtual void ProcessLine(IEnumerable<double> numbers, MCUBoard board, SensorSample[] targetSamples)
        {
            int i = 1;
            var timestamp = DateTime.UtcNow;
            foreach (var value in numbers)
            {
                var sensor = targetSamples.SingleOrDefault(x => x.Input.PortNumber == i);
                if (sensor != null)
                {
                    sensor.Value = value;
                    DetectAndWarnSensorDisconnects(board, sensor);
                }

                i++;
            }
        }

        private void DetectAndWarnSensorDisconnects(MCUBoard board, SensorSample sensor)
        {
            if (!sensor.HasSpecialDisconnectValue())
            {//we reset the attempts when we get valid values, both to avoid recurring but temporary errors firing the warning + to re-enable the warning when the issue is fixed.
                sensor.InvalidReadsRemainingAttempts = 3000;
                return;
            }

            var remainingAttempts = sensor.InvalidReadsRemainingAttempts;
            if (ExactSensorAttemptsCheck(ref remainingAttempts))
                LogError(board, $"sensor {sensor.Name} has been unreachable for at least 5 minutes (returning 10k+ values)");

            sensor.InvalidReadsRemainingAttempts = remainingAttempts;
        }

        public void ProcessLine(IEnumerable<double> numbers, MCUBoard board) => ProcessLine(numbers, board, GetSamples(board));
        private SensorSample[] GetSamples(MCUBoard board)
        {
            if (_boardSamplesLookup.TryGetValue(board, out var samples))
                return samples;
            _boardSamplesLookup[board] = samples = _values.Where(s => s.Input.BoxName == board.BoxName).ToArray();
            return samples;
        }


        private bool HelpMenu(List<string> args)
        {
            CALog.LogInfoAndConsoleLn(LogID.A, commandHelp);
            return true;
        }

        private void LogError(IOconfMap board, string message, Exception ex) 
        {
            if (board.Board != null)
                LogError(board.Board, message, ex);
            else
                CALog.LogErrorAndConsoleLn(LogID.A, $"{message} - {Title} - {board.BoxName} (missing)", ex); 
        }
        private void LogError(MCUBoard board, string message, Exception ex) => CALog.LogErrorAndConsoleLn(LogID.A, $"{message} - {Title} - {board.ToShortDescription()}", ex);
        private void LogError(MCUBoard board, string message) => CALog.LogErrorAndConsoleLn(LogID.A, $"{message} - {Title} - {board.ToShortDescription()}");
        private void LogData(MCUBoard board, string message) => CALog.LogData(LogID.B, $"{message} - {Title} - {board.ToShortDescription()}");
        private void LogInfo(MCUBoard board, string message) => CALog.LogInfoAndConsoleLn(LogID.B, $"{message} - {Title} - {board.ToShortDescription()}");
        protected virtual void Dispose(bool disposing) => _boardLoopsStopTokenSource.Cancel();
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method. See https://docs.microsoft.com/en-us/dotnet/standard/garbage-collection/implementing-dispose#dispose-and-disposebool
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        protected class AllBoardsState : IEnumerable<(string sensorName, ConnectionState State)>
        {
            private readonly ConnectionState[] _states;
            private readonly string[] _sensorNames;
            private readonly Dictionary<string, int> _boardsIndexes;

            public AllBoardsState(IEnumerable<IOconfMap> boards)
            {
                // taking a copy of the list ensures we can keep reporting the state of the board, as otherwise the calling code can remove it, for example, when it decides to ignore the board
                var _boardList = boards.ToList(); 
                _states = new ConnectionState[_boardList.Count];
                _sensorNames = new string[_boardList.Count];
                _boardsIndexes = new Dictionary<string, int>(_boardList.Count);
                for (int i = 0; i < _boardList.Count; i++)
                {
                    _sensorNames[i] = _boardList[i].BoxName + "_state";
                    _boardsIndexes[_boardList[i].BoxName] = i;
                    _states[i] = _boardList[i].Board?.InitialConnectionSucceeded == true ? ConnectionState.Connected : ConnectionState.Disconnected;
                }
            }

            public IEnumerator<(string, ConnectionState)> GetEnumerator() 
            {
                for (int i = 0; i < _sensorNames.Length; i++)
                    yield return (_sensorNames[i], _states[i]);
            }

            public void SetReadSensorsExceptionState(MCUBoard board) => SetState(board, ConnectionState.ReadError);
            public void SetAttemptingReconnectState(MCUBoard board) => SetState(board, ConnectionState.Connecting);
            public void SetDisconnectedState(MCUBoard board) => SetState(board, ConnectionState.Disconnected);
            public void SetDisconnectedState(IOconfMap board) => SetState(board, ConnectionState.Disconnected);
            public void SetConnectedState(MCUBoard board) => SetState(board, ConnectionState.Connected);
            public void SetState(MCUBoard board, ConnectionState state) => _states[_boardsIndexes[board.BoxName]] = state;
            public void SetState(IOconfMap board, ConnectionState state) => _states[_boardsIndexes[board.BoxName]] = state;

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        public enum ConnectionState
        {
            NodeUnreachable = -1, // can be used by distributed deployments to indicate the node that has the board is unreachable
            Disconnected = 0, // we are not currently connected to the board
            Connecting = 1, // we are attempting to reconnect to the board
            Connected = 2, // we have succesfully connected to the board and will soon be attempting to read from it
            ReadError = 3, // there are unexpected exceptions communicating with the board
            NoDataAvailable = 4, // we are connected to the box, but we have not received for 150ms+
            ReturningNonValues = 5, // we are getting data from the box, but these are not values lines
            ReceivingValues = 6 // we are connected & receiving values
        }
    }
}
