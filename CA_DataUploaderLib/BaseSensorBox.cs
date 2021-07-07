using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Text;
using CA_DataUploaderLib.IOconf;
using System.Text.RegularExpressions;
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
                    CALog.LogErrorAndConsoleLn(LogID.A, $"error closing the connection to board {board}", ex);
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
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(msBetweenReads, token);
                    await SafeReadSensors(board, targetSamples, token); 
                    if (CheckFails(board, targetSamples))
                        await ReconnectBoard(board, targetSamples, token);
                }
                catch (TaskCanceledException ex)
                {
                    if (!token.IsCancellationRequested)
                        CALog.LogErrorAndConsoleLn(LogID.A, ex.ToString());
                }
                catch (Exception ex)
                { // we expect most errors to be handled within SafeReadSensors and in the SafeReopen of the ReconnectBoard,
                  // so seeing this in the log is most likely a bug handling some error case.
                    _allBoardsState.SetReadSensorsExceptionState(board);
                    CALog.LogErrorAndConsoleLn(LogID.A, $"unhandled error for {Title}-{board}", ex);
                }
            }
        }

        private async Task SafeReadSensors(MCUBoard board, SensorSample[] targetSamples, CancellationToken token)
        {
            try
            {
                await ReadSensors(board, targetSamples, token);
            }
            catch (Exception ex)
            { // seeing this is the log is not unexpected in cases where we have trouble communicating to a board.
                _allBoardsState.SetReadSensorsExceptionState(board);
                CALog.LogErrorAndConsoleLn(LogID.A, "error reading data from {Title}-{board}", ex);
            }
        }

        private async Task ReadSensors(MCUBoard board, SensorSample[] targetSamples, CancellationToken token)
        {
            bool hadDataAvailable = false, receivedValues = false;
            var timeInLoop = Stopwatch.StartNew();
            // We read all lines available, but only those we can start within 100ms to avoid being stuck when data with errors is continuously returned by the board.
            // We use time instead of attempts as SafeHasDataInReadBuffer can continuously report there is data when a partial line is returned by a board that stalls,
            // which then consistently times out in SafeReadLine, making each loop iteration 2 seconds (the regular read timeout).
            while (await board.SafeHasDataInReadBuffer(token) && timeInLoop.ElapsedMilliseconds < 100)
            {
                hadDataAvailable = true;
                var line = await board.SafeReadLine(token); // tries to read a full line for up to MCUBoard.ReadTimeout
                try
                {
                    var numbers = TryParseAsDoubleList(board, line);
                    if (numbers != null)
                    {
                        ProcessLine(numbers, board, targetSamples);
                        receivedValues = true;
                    }
                    else if (!board.ConfigSettings.Parser.IsExpectedNonValuesLine(line))// mostly responses to commands or headers on reconnects.
                        CALog.LogInfoAndConsoleLn(LogID.B, $"Unexpected board response {board}: {line}");
                }
                catch (Exception ex)
                {
                    CALog.LogErrorAndConsoleLn(LogID.B, $"Failed handling board response {board}: " + line, ex);
                }
            }

            _allBoardsState.SetReadSensorsState(board, hadDataAvailable, receivedValues);
            if (!hadDataAvailable) 
                // we expect data on every cycle (each 100 ms), as the boards normally write a line every 100 ms.
                CALog.LogData(LogID.B, "No data available for " + board.ToString());
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
                CALog.LogErrorAndConsoleLn(LogID.A, $"Stale sensor detected: {Title} - {item.Input.Name}. {msSinceLastRead} milliseconds since last read");
            }

            return hasStaleValues;
        }

        private async Task ReconnectBoard(MCUBoard board, SensorSample[] targetSamples, CancellationToken token)
        {
            _allBoardsState.SetAttemptingReconnectState(board);
            CALog.LogInfoAndConsoleLn(LogID.A, $"Attempting to reconnect to {Title} - {board}");
            var lostSensorAttempts = 100;
            var delayBetweenAttempts = TimeSpan.FromSeconds(board.ConfigSettings.SecondsBetweenReopens);
            while (!(await board.SafeReopen(token)))
            {
                _allBoardsState.SetDisconnectedState(board);
                if (ExactSensorAttemptsCheck(ref lostSensorAttempts)) 
                { // we run this once when there has been 100 attempts
                    var reconnecMsg = $"{Title} - {board}: reconnect limit exceeded, reducing reconnect frequency to 15 minutes";
                    CALog.LogErrorAndConsoleLn(LogID.A, reconnecMsg);
                    _cmd.FireAlert(reconnecMsg);
                    delayBetweenAttempts = TimeSpan.FromMinutes(15); // 4 times x hour = 96 times x day
                    if (board.ConfigSettings.StopWhenLosingSensor)
                    {
                        CALog.LogErrorAndConsoleLn(LogID.A, $"Emergency shutdown: {Title}-{board} reconnect limit exceeded");
                        _cmd.Execute("emergencyshutdown");
                    }
                }

                await Task.Delay(delayBetweenAttempts, token);
            }

            _allBoardsState.SetConnectedState(board);
            CALog.LogInfoAndConsoleLn(LogID.A, $"{Title} - {board}: board reconnection succeeded.");
        }

        private bool ExactSensorAttemptsCheck(ref int lostSensorAttempts)
        {
             if (lostSensorAttempts > 0)
                lostSensorAttempts--;
            else if (lostSensorAttempts == 0) 
                return true;
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

                    HandleSaltLeakage(sensor);
                }

                i++;
            }
        }
        public void ProcessLine(IEnumerable<double> numbers, MCUBoard board) => ProcessLine(numbers, board, GetSamples(board));
        private SensorSample[] GetSamples(MCUBoard board)
        {
            if (_boardSamplesLookup.TryGetValue(board, out var samples))
                return samples;
            _boardSamplesLookup[board] = samples = _values.Where(s => s.Input.BoxName == board.BoxName).ToArray();
            return samples;
        }

        private void HandleSaltLeakage(SensorSample sensor)
        {
            if (sensor.GetType() == typeof(IOconfSaltLeakage))
            {
                if (sensor.Value < 3000 && sensor.Value > 0)  // Salt leakage algorithm. 
                {
                    CALog.LogErrorAndConsoleLn(LogID.A, $"Salt leak detected from {sensor.Input.Name}={sensor.Value} {DateTime.Now:dd-MMM-yyyy HH:mm}");
                    sensor.Value = 1d;
                    if (_cmd != null)
                        _cmd.Execute("emergencyshutdown"); // make the whole system shut down. 
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
            public void SetReadSensorsState(MCUBoard board, bool hadDataAvailable, bool receivedValues)
            {
                var lastState = GetLastState(board);
                var newState = 
                    receivedValues ? ConnectionState.ReceivingValues :
                    hadDataAvailable && lastState == ConnectionState.Connecting ? ConnectionState.Connecting :
                    hadDataAvailable ? ConnectionState.ReturningNonValues : 
                    ConnectionState.NoDataAvailable;
                if (lastState == newState) return;
                SetState(board, newState);
            }

            private void SetState(MCUBoard board, ConnectionState state) => _states[_boardsIndexes[board]] = state;
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
