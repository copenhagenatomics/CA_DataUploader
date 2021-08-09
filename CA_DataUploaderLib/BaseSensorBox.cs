using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Text;
using CA_DataUploaderLib.IOconf;
using Humanizer;
using System.Diagnostics;
using CA_DataUploaderLib.Extensions;
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
        protected readonly List<SensorSample> _values = new List<SensorSample>();
        protected readonly AllBoardsState _allBoardsState;
        private readonly string commandHelp;
        private readonly CancellationTokenSource _boardLoopsStopTokenSource = new CancellationTokenSource();
        private readonly Dictionary<MCUBoard, SensorSample[]> _boardSamplesLookup = new Dictionary<MCUBoard, SensorSample[]>();

        public BaseSensorBox(
            CommandHandler cmd, string commandName, string commandArgsHelp, string commandDescription, IEnumerable<IOconfInput> values)
        { 
            Title = commandName;
            _cmd = cmd;
            _values = values.IsInitialized().Select(x => new SensorSample(x)).ToList();
            if (!_values.Any())
                return;  // no data

            if (cmd != null)
            {
                commandHelp = $"{commandName.ToLower() + " " + commandArgsHelp,-26}- {commandDescription}";
                cmd.AddCommand(commandName.ToLower(), ShowQueue);
                cmd.AddCommand("help", HelpMenu);
                cmd.AddCommand("escape", Stop);
                cmd.AddSubsystem(this);
            }

            var boards = _values.Where(x => !x.Input.Skip).GroupBy(x => x.Input.Map.Board).Select(g => (board: g.Key, values: g.ToArray())).ToArray();
            _allBoardsState = new AllBoardsState(boards.Select(b => b.board));
            Task.Run(() => RunBoardReadLoops(boards));
        }

        public IEnumerable<SensorSample> GetInputValues() => _values
            .Select(s => s.Clone())
            .Concat(_allBoardsState.Select(b => new SensorSample(b.sensorName, (int)b.State)));

        public IEnumerable<SensorSample> GetDecisionOutputs(NewVectorReceivedArgs inputVectorReceivedArgs) => Enumerable.Empty<SensorSample>();
        public virtual List<VectorDescriptionItem> GetVectorDescriptionItems() =>
            _values
                .Select(x => new VectorDescriptionItem("double", x.Input.Name, DataTypeEnum.Input))
                .Concat(_allBoardsState.Select(b => new VectorDescriptionItem("double", b.sensorName, DataTypeEnum.State)))
                .ToList();

        protected bool ShowQueue(List<string> args)
        {
            var sb = new StringBuilder($"NAME      {GetAvgLoopTime(),4:N0}           ");
            sb.AppendLine();
            foreach (var t in _values)
            {
                sb.AppendLine($"{t.Input.Name,-22}={t.Value,9:N2}");
            }

            CALog.LogInfoAndConsoleLn(LogID.A, sb.ToString());
            return true;
        }

        private double GetAvgLoopTime() => _values.Average(x => x.ReadSensor_LoopTime);

        private async Task RunBoardReadLoops((MCUBoard board, SensorSample[] values)[] boards)
        {
            DateTime start = DateTime.Now;
            try
            {
                var readLoops = StartReadLoops(boards, _boardLoopsStopTokenSource.Token);
                await Task.WhenAll(readLoops);
                CALog.LogInfoAndConsoleLn(LogID.A, $"Exiting {Title}.RunBoardReadLoops() " + DateTime.Now.Subtract(start).Humanize(5));
            }
            catch (Exception ex)
            {
                CALog.LogErrorAndConsoleLn(LogID.A, ex.ToString());
            }

            Stopping?.Invoke(this, EventArgs.Empty);
            foreach (var board in boards)
            {
                try
                {
                    board.board?.SafeClose(CancellationToken.None);
                    _allBoardsState.SetDisconnectedState(board.board);
                }
                catch(Exception ex)
                {
                    LogError(board.board, "error closing the connection to the board", ex);
                }
            }
        }

        protected virtual List<Task> StartReadLoops((MCUBoard board, SensorSample[] values)[] boards, CancellationToken token)
        {
            return boards
                .Select(b => BoardLoop(b.board, b.values, token))
                .ToList();
        }

        private async Task BoardLoop(MCUBoard board, SensorSample[] targetSamples, CancellationToken token)
        {
            var msBetweenReads = board.ConfigSettings.MillisecondsBetweenReads;
            var readThrottle = new TimeThrottle(msBetweenReads);
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await readThrottle.WaitAsync();
                    var stillConnected = await SafeReadSensors(board, targetSamples, msBetweenReads, token);
                    if (!stillConnected || CheckFails(board, targetSamples))
                        await ReconnectBoard(board, targetSamples, token);
                }
                catch (TaskCanceledException ex)
                { //if the token is canceled we are about to exit the loop so we do nothing. Otherwise we consider it like any other exception and log it.
                    if (!token.IsCancellationRequested)
                    {
                        _allBoardsState.SetReadSensorsExceptionState(board);
                        LogError(board, "unexpected error on board read loop", ex);
                    }
                }
                catch (Exception ex)
                { // we expect most errors to be handled within SafeReadSensors and in the SafeReopen of the ReconnectBoard,
                  // so seeing this in the log is most likely a bug handling some error case.
                    _allBoardsState.SetReadSensorsExceptionState(board);
                    LogError(board, "unexpected error on board read loop", ex);
                }
            }
        }

        ///<returns><c>false</c> if a board disconnect was detected</returns>
        private async Task<bool> SafeReadSensors(MCUBoard board, SensorSample[] targetSamples, int msBetweenReads, CancellationToken token)
        { //we use this to prevent read exceptions from interfering with failure checks and reconnects
            try
            {
                return await ReadSensors(board, targetSamples, msBetweenReads, token);
            }
            catch (TaskCanceledException ex)
            { 
                if (token.IsCancellationRequested)
                    throw; // if the token is cancelled we bubble up the cancel so the caller can abort.
                _allBoardsState.SetReadSensorsExceptionState(board);
                LogError(board, "error reading sensor data", ex);
                return true; //ReadSensor normally should return false if the board is detected as disconnected, so we say the board is still connected here since the caller will still do stale values detection
            }
            catch (Exception ex)
            { // seeing this is the log is not unexpected in cases where we have trouble communicating to a board.
                _allBoardsState.SetReadSensorsExceptionState(board);
                LogError(board, "error reading sensor data", ex);
                return true; //ReadSensor normally should return false if the board is detected as disconnected, so we say the board is still connected here since the caller will still do stale values detection
            }
        }

        ///<returns><c>false</c> if a board disconnect was detected</returns>
        private async Task<bool> ReadSensors(MCUBoard board, SensorSample[] targetSamples, int msBetweenReads, CancellationToken token)
        {
            bool receivedValues = false;
            var timeInLoop = Stopwatch.StartNew();
            //We read all lines available, but only those we can start within msBetweenReads to avoid being stuck when data with errors is continuously returned by the board.
            //We set the state early if we detect no data is being returned or if we received values,
            //but we only set ReturningNonValues until we have confirmed there is no valid data in the rest of available data read within msBetweenReads.
            do
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
                        receivedValues = true;
                        _allBoardsState.SetState(board, ConnectionState.ReceivingValues);
                    }
                    else if (!board.ConfigSettings.Parser.IsExpectedNonValuesLine(line))// mostly responses to commands or headers on reconnects.
                        LogInfo(board, $"unexpected board response {line.Replace("\r", "\\r")}"); // we avoid \r as it makes the output hard to read
                }
                catch (Exception ex)
                { //usually a parsing errors on non value data, we log it and consider it as such i.e. we finish reading available data and set ReturningNonValues if we did not get valid values
                    LogError(board, $"failed handling board response {line.Replace("\r", "\\r")}", ex); // we avoid \r as it makes the output hard to read
                }
            }
            while (await board.SafeHasDataInReadBuffer(token) && timeInLoop.ElapsedMilliseconds < msBetweenReads);

            if (!receivedValues)
                _allBoardsState.SetState(board, ConnectionState.ReturningNonValues);
            return true; //still connected
        }

        ///<returns>(<c>false</c> if the board was explicitely detected as disconnected, the line, or null/default string if it exceeded the MCUBoard.ReadTimeout).</returns>
        ///<remarks>
        ///Notifies in _allBoardsState (which is reported to the next vector) if there is no data available within <param cref="msBetweenReads" /> 
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
                _allBoardsState.SetState(board, ConnectionState.NoDataAvailable); //report the state early before waiting up to 2 seconds for the data (readLineTask)
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

        private async Task ReconnectBoard(MCUBoard board, SensorSample[] targetSamples, CancellationToken token)
        {
            _allBoardsState.SetAttemptingReconnectState(board);
            LogInfo(board, "attempting to reconnect");
            var lostSensorAttempts = 100;
            var delayBetweenAttempts = TimeSpan.FromSeconds(board.ConfigSettings.SecondsBetweenReopens);
            while (!(await board.SafeReopen(token)))
            {
                _allBoardsState.SetDisconnectedState(board);
                if (ExactSensorAttemptsCheck(ref lostSensorAttempts)) 
                { // we run this once when there has been 100 attempts
                    var reconnecMsg = $"reconnect limit exceeded, reducing reconnect frequency to 15 minutes";
                    LogError(board, reconnecMsg);
                    _cmd.FireAlert(reconnecMsg + " - title - " + board.ToShortDescription());
                    delayBetweenAttempts = TimeSpan.FromMinutes(15); // 4 times x hour = 96 times x day
                    if (board.ConfigSettings.StopWhenLosingSensor)
                    {
                        LogError(board, "emergency shutdown: reconnect limit exceeded");
                        _cmd.Execute("emergencyshutdown");
                    }
                }

                await Task.Delay(delayBetweenAttempts, token);
            }

            _allBoardsState.SetConnectedState(board);
            LogInfo(board, "board reconnection succeeded");
        }

        private bool ExactSensorAttemptsCheck(ref int lostSensorAttempts)
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
                    HandleSaltLeakage(board, sensor);
                }

                i++;
            }
        }

        private void DetectAndWarnSensorDisconnects(MCUBoard board, SensorSample sensor)
        {
            if (sensor.Value < 10000)
            {//we reset the attempts when we get valid values, both to avoid recurring but temporary errors firing the warning + to re-enable the warning when the issue is fixed.
                sensor.InvalidReadsRemainingAttempts = 3000;
                return;
            }

            var remainingAttempts = sensor.InvalidReadsRemainingAttempts;
            if (ExactSensorAttemptsCheck(ref remainingAttempts))
            {
                var lostSensorMsg = $"sensor {sensor.Name} has been unreachable for at least 5 minutes (returning 10k+ values)";
                LogError(board, lostSensorMsg);
                _cmd.FireAlert(lostSensorMsg + " - title - " + board.ToShortDescription());
            }

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

        private void HandleSaltLeakage(MCUBoard board, SensorSample sensor)
        {
            if (sensor.GetType() == typeof(IOconfSaltLeakage))
            {
                if (sensor.Value < 3000 && sensor.Value > 0)  // Salt leakage algorithm. 
                {
                    LogError(board, $"salt leak detected from {sensor.Input.Name}={sensor.Value} {DateTime.Now:dd-MMM-yyyy HH:mm}");
                    sensor.Value = 1d;
                    if (_cmd != null)
                    {
                        LogError(board, "emergency shutdown: salt leakage detected");
                        _cmd.Execute("emergencyshutdown"); // make the whole system shut down. 
                    }
                }
                else
                {
                    sensor.Value = 0d; // no leakage
                }
            }
        }

        private bool HelpMenu(List<string> args)
        {
            CALog.LogInfoAndConsoleLn(LogID.A, commandHelp);
            return true;
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
            private readonly List<MCUBoard> _boards;
            private readonly ConnectionState[] _states;
            private readonly string[] _sensorNames;
            private readonly Dictionary<MCUBoard, int> _boardsIndexes;

            public AllBoardsState(IEnumerable<MCUBoard> boards)
            {
                // taking a copy of the list ensures we can keep reporting the state of the board, as otherwise the calling code can remove it, for example, when it decides to ignore the board
                _boards = boards.ToList(); 
                _states = new ConnectionState[_boards.Count];
                _sensorNames = new string[_boards.Count];
                _boardsIndexes = new Dictionary<MCUBoard, int>(_boards.Count);
                for (int i = 0; i < _boards.Count; i++)
                {
                    _sensorNames[i] = _boards[i].BoxName + "_state";
                    _boardsIndexes[_boards[i]] = i;
                    _states[i] = ConnectionState.Connected;
                }
            }

            public IEnumerator<(string, ConnectionState)> GetEnumerator() 
            {
                for (int i = 0; i < _boards.Count; i++)
                    yield return (_sensorNames[i], _states[i]);
            }

            public void SetReadSensorsExceptionState(MCUBoard board) => SetState(board, ConnectionState.ReadError);
            public void SetAttemptingReconnectState(MCUBoard board) => SetState(board, ConnectionState.Connecting);
            public void SetDisconnectedState(MCUBoard board) => SetState(board, ConnectionState.Disconnected);
            public void SetConnectedState(MCUBoard board) => SetState(board, ConnectionState.Connected);
            public void SetState(MCUBoard board, ConnectionState state) => _states[_boardsIndexes[board]] = state;
            private ConnectionState GetLastState(MCUBoard board) => _states[_boardsIndexes[board]];

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        public enum ConnectionState
        {
            Disconnected = 0,
            Connecting = 1,
            Connected = 2,
            ReadError = 3,
            NoDataAvailable = 4,
            ReturningNonValues = 5, // we are getting data from the box, but these are not values lines
            ReceivingValues = 6
        }
    }
}
