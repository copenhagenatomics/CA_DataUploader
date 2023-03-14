﻿#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Text;
using CA_DataUploaderLib.IOconf;
using Humanizer;
using System.Diagnostics;
using System.Collections;
using System.Threading.Tasks;
using System.Threading.Channels;

namespace CA_DataUploaderLib
{
    public class BaseSensorBox : ISubsystemWithVectorData
    {
        public string Title { get; protected set; }
        /// <summary>runs when the subsystem is about to stop running, but before all boards are closed</summary>
        /// <remarks>some boards might be closed, specially if the system is stopping due to losing connection to one of the boards</remarks>
        public event EventHandler? Stopping;
        private readonly CommandHandler _cmd;
        protected readonly List<SensorSample.InputBased> _values;
        protected readonly List<SensorSample.InputBased> _localValues;
        private readonly (IOconfMap map, SensorSample.InputBased[] values, int boardStateIndexInFullVector)[] _boards = Array.Empty<(IOconfMap map, SensorSample.InputBased[] values, int boardStateIndexInFullVector)>();
        protected readonly AllBoardsState _boardsState = new(Enumerable.Empty<IOconfMap>());
        private readonly Dictionary<MCUBoard, SensorSample.InputBased[]> _boardSamplesLookup = new();
        private readonly string mainSubsystem;
        private readonly Dictionary<MCUBoard, List<(Func<DataVector?, MCUBoard, CancellationToken, Task> write, Func<MCUBoard, CancellationToken, Task> exit)>> _buildInWriteActions = new();
        private readonly Dictionary<MCUBoard, ChannelReader<string>> _boardCustomCommandsReceived = new();
        private ChannelReader<DataVector>? _receivedVectors;

        public BaseSensorBox(CommandHandler cmd, string commandName, IEnumerable<IOconfInput> values)
        { 
            mainSubsystem = commandName.ToLower();
            Title = commandName;
            _cmd = cmd;
            _values = values.Select(x => new SensorSample.InputBased(x)).ToList();
            _localValues = _values.Where(x => x.Input.Map.IsLocalBoard).ToList();
            if (!_values.Any())
                return;  // no data

            if (cmd != null)
            {
                SubscribeCommandsToSubsystems(cmd, mainSubsystem, _values);
                cmd.AddSubsystem(this);
                cmd.FullVectorIndexesCreated += InitializeBuiltInActionsIndexesAndVectorsChannel;
            }

            var allBoards = _values.Where(x => !x.Input.Skip).GroupBy(x => x.Input.Map).Select(g => (map: g.Key, values: g.ToArray(), boardStateIndexInFullVector: -1)).ToArray();
            foreach (var board in allBoards.Where(b => b.map.CustomWritesEnabled))
                RegisterCustomBoardCommand(board.map, cmd, _boardCustomCommandsReceived);
            _boards = allBoards.Where(b => b.map.IsLocalBoard).ToArray();
            _boardsState = new AllBoardsState(_boards.Select(b => b.map));

            static void RegisterCustomBoardCommand(IOconfMap map, CommandHandler? cmd, Dictionary<MCUBoard, ChannelReader<string>> boardCustomCommandsReceived)
            {
                if (!map.IsLocalBoard)
                {//if the board is not local we register the command validation with an empty action
                    cmd.AddMultinodeCommand("custom", a => a.Count >= 3 && a[1] == map.BoxName, _ => { });
                    return;
                }

                if (map.McuBoard == null)
                {
                    CALog.LogData(LogID.A, $"missing local board detected with custom writes enabled: {map.BoxName}"); //the missing board will be already reported to the user later.
                    cmd.AddMultinodeCommand(
                        "custom", a => a.Count >= 3 && a[1] == map.BoxName, 
                        _ => { CALog.LogData(LogID.A, $"custom command failed due to missing board: {map.BoxName}"); });
                    return;
                }

                //for local boards we instead register a multinode that sends the command to a local channel the write loop uses
                var channel = Channel.CreateUnbounded<string>();
                var channelWriter = channel.Writer;
                boardCustomCommandsReceived.Add(map.McuBoard, channel.Reader);
                cmd.AddMultinodeCommand(
                    "custom", 
                    a => a.Count >= 3 && a[1] == map.BoxName, 
                    a => channelWriter.TryWrite(string.Join(' ', a.Skip(2))));
            }
        }

        private void InitializeBuiltInActionsIndexesAndVectorsChannel(object? _, IReadOnlyDictionary<string, int> indexes)
        {
            bool hasBoardWithBuildInActions = false;
            for (int i = 0; i < _boards.Length; i++)
            {
                var (map, values, _) = _boards[i];
                string name = map.BoxName + "_state";
                if (!indexes.TryGetValue(name, out var index)) throw new ArgumentException($"failed to find box state in full vector: {name}");
                _boards[i] = (map, values, index);
                hasBoardWithBuildInActions |= map.McuBoard != null && _buildInWriteActions.TryGetValue(map.McuBoard, out var _);
            }

            if (hasBoardWithBuildInActions)
                _receivedVectors = _cmd.GetReceivedVectorsReader();
        }

        public Task Run(CancellationToken token) => RunBoardLoops(_boards, token);
        private void SubscribeCommandsToSubsystems(CommandHandler cmd, string mainSubsystem, List<SensorSample.InputBased> values)
        {
            cmd.AddMultinodeCommand(mainSubsystem, _ => true, ShowQueue);
            var subsystemOverrides = values.Select(v => v.Input.SubsystemOverride).Where(v => v != default).Distinct();
            foreach (var subsystem in subsystemOverrides)
            {
                if (subsystem == mainSubsystem) continue;
                cmd.AddMultinodeCommand(subsystem, _ => true, ShowQueue);
            }
        }

        public IEnumerable<SensorSample> GetInputValues() => _localValues
            .Select(s => s.Clone())
            .Concat(_boardsState.Select(b => new SensorSample(b.sensorName, (int)b.State)));

        public virtual SubsystemDescriptionItems GetVectorDescriptionItems() => GetVectorDescriptionItems(_values);
        public static SubsystemDescriptionItems GetVectorDescriptionItems(List<SensorSample.InputBased> values)
        {
            var nodes = values.GroupBy(v => v.Input.Map.DistributedNode);
            var valuesByNode = nodes.Select(n => (n.Key, GetNodeDescItems(n))).ToList();
            return new SubsystemDescriptionItems(valuesByNode);

            static List<VectorDescriptionItem> GetNodeDescItems(IEnumerable<SensorSample.InputBased> values) =>
                values.Select(v => new VectorDescriptionItem("double", v.Input.Name, DataTypeEnum.Input) { Upload = v.Input.Upload })
                 .Concat(GetBoards(values).Select(b => new VectorDescriptionItem("double", b.BoxName + "_state", DataTypeEnum.State)))
                 .ToList();
            static IEnumerable<IOconfMap> GetBoards(IEnumerable<SensorSample.InputBased> n) =>
                n.Where(v => !v.Input.Skip).GroupBy(v => v.Input.Map).Select(b => b.Key);
        }

        /// <remarks>must be called before <see cref="CommandHandler.FullVectorDescriptionCreated"/> and <see cref="Run"/> are called</remarks>
        public void AddBuildInWriteAction(MCUBoard board, Func<DataVector?, MCUBoard, CancellationToken, Task> writeAction, Func<MCUBoard, CancellationToken, Task> exitAction)
        {
            if (!_buildInWriteActions.TryGetValue(board, out var actions)) _buildInWriteActions[board] = new() { (writeAction, exitAction ) };
            else actions.Add((writeAction, exitAction));
        }

        protected void RegisterBoardWriteActions(
            MCUBoard board, IOconfOutput port, double defaultTarget, string targetFieldName, 
            Func<int, double, string> getCommand, int repeatMilliseconds = 2000)
        {
            var lastAction = new LastAction(defaultTarget, repeatMilliseconds);
            var fieldIndex = -1;
            _cmd.FullVectorIndexesCreated += InitializeAction;
            AddBuildInWriteAction(board, WriteAction, ExitAction);

            void InitializeAction(object? sender, IReadOnlyDictionary<string, int> indexes) =>
                fieldIndex = indexes.TryGetValue(targetFieldName, out var index) 
                    ? index 
                    : throw new InvalidOperationException($"missing target field: {targetFieldName}");
            Task ExitAction(MCUBoard board, CancellationToken token) => Off(board, port, token);
            Task WriteAction(DataVector? vector, MCUBoard board, CancellationToken token) => DoPortActions(vector, board, fieldIndex, lastAction, token);

            async Task DoPortActions(DataVector? vector, MCUBoard board, int fieldIndex, LastAction lastAction, CancellationToken token)
            {
                if (vector == null)
                {
                    await board.SafeWriteLine(getCommand(port.PortNumber, defaultTarget), token);
                    lastAction.TimedOutWaittingForDecision(defaultTarget);
                    return;
                }

                var target = vector[fieldIndex];
                if (!lastAction.ChangedOrExpired(target, vector.Timestamp))
                    return;

                await board.SafeWriteLine(getCommand(port.PortNumber, target), token);
                lastAction.ExecutedNewAction(target, vector.Timestamp);
            }

            async Task Off(MCUBoard board, IOconfOutput port, CancellationToken token)
            {
                await board.SafeWriteLine(getCommand(port.PortNumber, defaultTarget), token);
                CALog.LogInfoAndConsoleLn(LogID.A, $"port has been set to default position ({defaultTarget:F2}): {port.Name}");
            }
        }

        private void ShowQueue(List<string> args)
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
                string? subsystemOverride = t.Input.SubsystemOverride;
                var matchesSubsystem = (isMainCommand && subsystemOverride == default) || subsystemOverride == subsystem;
                if (matchesSubsystem)
                    sb.AppendLine($"{t.Input.Name,-22}={t.Value,9:N2}");
            }

            CALog.LogInfoAndConsoleLn(LogID.A, sb.ToString());
        }

        private double GetAvgLoopTime() => _values.Average(x => x.ReadSensor_LoopTime);
        private async Task RunBoardLoops((IOconfMap map, SensorSample.InputBased[] values, int boardStateIndexInFullVector)[] boards, CancellationToken token)
        {
            DateTime start = DateTime.Now;
            try
            {
                var loops = StartLoops(boards, token);
                await Task.WhenAll(loops);
                if (loops.Count > 0) //we only report the exit when we actually ran loops with detected boards. If a board was not detected StartReadLoops already reports the missing boards.
                    CALog.LogInfoAndConsoleLn(LogID.A, $"Exiting {Title}.RunBoardLoops() " + DateTime.Now.Subtract(start).Humanize(5));
            }
            catch (Exception ex)
            {
                CALog.LogErrorAndConsoleLn(LogID.A, $"{Title} - unexpected error detected", ex);
            }

            Stopping?.Invoke(this, EventArgs.Empty);
            foreach (var (map, _, _) in boards)
            {
                try
                {
                    using var closeTimeoutToken = new CancellationTokenSource(5000);
                    map.McuBoard?.SafeClose(closeTimeoutToken.Token);
                    _boardsState.SetDisconnectedState(map);
                }
                catch(Exception ex)
                {
                    LogError(map, "error closing the connection to the board", ex);
                }
            }
        }

        protected virtual List<Task> StartLoops((IOconfMap map, SensorSample.InputBased[] values, int boardStateIndexInFullVector)[] boards, CancellationToken token)
        {
            var missingBoards = boards.Where(h => h.map.McuBoard == null).Select(b => b.map.BoxName).Distinct().ToList();
            if (missingBoards.Count > 0)
                CALog.LogErrorAndConsoleLn(LogID.A, $"{Title} - missing boards detected {string.Join(",", missingBoards)}. Related sensors/actuators are disabled.");

            return boards
                .Where(b => b.map.McuBoard != null) //we ignore the missing boards for now as we don't have auto reconnect logic yet for boards not detected during system start.
                .SelectMany(b => new []{ BoardReadLoop(b.map.McuBoard!, b.map.BoxName, b.values, token), BoardWriteLoop(b.map.McuBoard!, b.boardStateIndexInFullVector, token) })
                .ToList();
        }

        private async Task BoardWriteLoop(MCUBoard board, int boardStateIndexInFullVector, CancellationToken token)
        {
            var customWritesEnabled = _boardCustomCommandsReceived.TryGetValue(board, out var customCommandsChannel);
            var buildInActionsEnabled = _buildInWriteActions.TryGetValue(board, out var buildInActions);
            if (!buildInActionsEnabled && !customWritesEnabled) return;
            ChannelReader<DataVector>? vectorsChannel = buildInActionsEnabled 
                ? (_receivedVectors ?? throw new InvalidOperationException("build actions detected without receiving vectors channel being initialized"))
                : null;

            // we use the next 2 booleans to avoid spamming logs/display with an ongoing problem, so we only notify at the beginning and when we resume normal operation.
            // we might still get lots of entries for problems that alternate between normal and failed states, but for now is a good data point to know if that case is happening.
            var waitingBoardReconnect = false;
            var tryingToRecoverAfterTimeoutWatch = new Stopwatch();

            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (customWritesEnabled && customCommandsChannel!.TryRead(out var command))
                        await board.SafeWriteLine(command, token);

                    if (!buildInActionsEnabled)
                    {
                        EnsureResumeAfterTimeoutIsReported();
                        await customCommandsChannel!.WaitToReadAsync(token);
                        continue;
                    }

                    var receivedVector = false;
                    do
                    {
                        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                        linkedCts.CancelAfter(2000);
                        var nextVectorTask = vectorsChannel!.ReadAsync(linkedCts.Token).AsTask();
                        await Task.WhenAny(nextVectorTask); //wrap on Task.Any so we can explicitely deal with global cancellation vs. timeout of the linked token
                        token.ThrowIfCancellationRequested(); //we are stopping, let's break of the top loop so stop actions run
                        receivedVector = nextVectorTask.IsCompletedSuccessfully;
                        var vector = receivedVector ? await nextVectorTask : null;
                        if (vector != null && !CheckConnectedStateInVector(board, boardStateIndexInFullVector, ref waitingBoardReconnect, vector))
                            continue; // no point trying to send commands while there is no connection to the board.

                        foreach (var (writeAction, _) in buildInActions!)
                            await writeAction(vector, board, token);

                        EnsureResumeAfterTimeoutIsReported();
                    }
                    while (!receivedVector);
                }
                catch (TimeoutException)
                {
                    EnsureTimeoutIsReportedOnce();
                    await Task.WhenAny(Task.Delay(500, token)); //reduce action frequency / the WhenAny avoids exceptions if the token is canceled
                }
                catch (OperationCanceledException ex) when (ex.CancellationToken == token)
                {}
                catch (Exception ex)
                {
                    CALog.LogErrorAndConsoleLn(LogID.A, $"Error detected writting to board: {board.ToShortDescription()}", ex);
                }
            }

            if (!buildInActionsEnabled) return;

            try
            {
                foreach (var (_, exitAction) in buildInActions!)
                {
                    using var timeoutToken = new CancellationTokenSource(3000);
                    await exitAction(board, timeoutToken.Token);
                }
            }
            catch (Exception ex)
            {
                CALog.LogErrorAndConsoleLn(LogID.A, $"Error running exit actions for board: {board.ToShortDescription()}", ex);
            }

            void EnsureTimeoutIsReportedOnce()
            {
                if (tryingToRecoverAfterTimeoutWatch.IsRunning) return;
                CALog.LogInfoAndConsoleLn(LogID.A, $"timed out writing to board, reducing action frequency until reconnect - {board.ToShortDescription()}");
                tryingToRecoverAfterTimeoutWatch.Restart();
            }

            void EnsureResumeAfterTimeoutIsReported()
            {
                if (!tryingToRecoverAfterTimeoutWatch.IsRunning) return;
                tryingToRecoverAfterTimeoutWatch.Stop();
                CALog.LogInfoAndConsoleLn(LogID.A, $"wrote to board without time outs after {tryingToRecoverAfterTimeoutWatch.Elapsed}, resuming normal action frequency - {board.ToShortDescription()}");
            }

            static bool CheckConnectedStateInVector(MCUBoard board, int boardState, ref bool waitingBoardReconnect, DataVector vector)
            {
                var vectorState = (ConnectionState)(int)vector[boardState];
                var connected = vectorState >= ConnectionState.Connected;
                if (waitingBoardReconnect && connected)
                {
                    CALog.LogData(LogID.B, $"resuming writes after reconnect on {board.ToShortDescription()}");
                    waitingBoardReconnect = false;
                }
                else if (!waitingBoardReconnect && !connected)
                {
                    CALog.LogData(LogID.B, $"stopping writes until connection is reestablished (state: {vectorState}) on: - {board.ToShortDescription()}");
                    waitingBoardReconnect = true;
                }
                return connected;
            }
        }

        private async Task BoardReadLoop(MCUBoard board, string boxName, SensorSample.InputBased[] targetSamples, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var stillConnected = await SafeReadSensors(board, boxName, targetSamples, token);
                    if (!stillConnected || CheckFails(board, targetSamples))
                        await ReconnectBoard(board, boxName, token);
                }
                catch (OperationCanceledException ex)
                { //if the token is canceled we are about to exit the loop so we do nothing. Otherwise we consider it like any other exception and log it.
                    if (!token.IsCancellationRequested)
                    {
                        _boardsState.SetReadSensorsExceptionState(boxName);
                        LogError(board, "unexpected error on board read loop", ex);
                    }
                }
                catch (Exception ex)
                { // we expect most errors to be handled within SafeReadSensors and in the SafeReopen of the ReconnectBoard,
                  // so seeing this in the log is most likely a bug handling some error case.
                    _boardsState.SetReadSensorsExceptionState(boxName);
                    LogError(board, "unexpected error on board read loop", ex);
                }
            }
        }

        ///<returns><c>false</c> if a board disconnect was detected</returns>
        private async Task<bool> SafeReadSensors(MCUBoard board, string boxName, SensorSample.InputBased[] targetSamples, CancellationToken token)
        { //we use this to prevent read exceptions from interfering with failure checks and reconnects
            try
            {
                return await ReadSensors(board, boxName, targetSamples, token);
            }
            catch (OperationCanceledException ex)
            { 
                if (token.IsCancellationRequested)
                    throw; // if the token is cancelled we bubble up the cancel so the caller can abort.
                _boardsState.SetReadSensorsExceptionState(boxName);
                LogError(board, "error reading sensor data", ex);
                return true; //ReadSensor normally should return false if the board is detected as disconnected, so we say the board is still connected here since the caller will still do stale values detection
            }
            catch (Exception ex)
            { // seeing this is the log is not unexpected in cases where we have trouble communicating to a board.
                _boardsState.SetReadSensorsExceptionState(boxName);
                LogError(board, "error reading sensor data", ex);
                return true; //ReadSensor normally should return false if the board is detected as disconnected, so we say the board is still connected here since the caller will still do stale values detection
            }
        }

        ///<returns><c>false</c> if a board disconnect was detected</returns>
        private async Task<bool> ReadSensors(MCUBoard board, string boxName, SensorSample.InputBased[] targetSamples, CancellationToken token)
        {
            var timeSinceLastValidRead = Stopwatch.StartNew();
            // we need to allow some extra time to avoid too aggressive reporting of boards not giving data, no particular reason for it being 50%.
            var msBetweenReads = (int)Math.Ceiling(board.ConfigSettings.MillisecondsBetweenReads * 1.5); 
            var timeSinceLastLog = new Stopwatch();
            var logSkipped = 0;
            //We set the state early if we detect no data is being returned or if we received values,
            //but we only set ReturningNonValues if it has passed msBetweenReads since the last valid read
            while (true) // we only stop reading if a disconnect or timeout is detected
            {
                var (stillConnected, line) = await TryReadLineWithStallDetection(board, boxName, msBetweenReads, token);
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
                        _boardsState.SetState(boxName, ConnectionState.ReceivingValues);
                    }
                    else if (!board.ConfigSettings.Parser.IsExpectedNonValuesLine(line))// mostly responses to commands or headers on reconnects.
                    {
                        LowFrequencyLog(msg => LogInfo(board, msg), $"unexpected board response {line.Replace("\r", "\\r")}");// we avoid \r as it makes the output hard to read
                        if (timeSinceLastValidRead.ElapsedMilliseconds > msBetweenReads)
                            _boardsState.SetState(boxName, ConnectionState.ReturningNonValues);
                    }
                }
                catch (Exception ex)
                { //usually a parsing errors on non value data, we log it and consider it as such i.e. we set ReturningNonValues if we have not had a valid read in msBetweenReads
                    LowFrequencyLog(msg => LogError(board, msg, ex), $"failed handling board response {line.Replace("\r", "\\r")}"); // we avoid \r as it makes the output hard to read
                    if (timeSinceLastValidRead.ElapsedMilliseconds > msBetweenReads)
                        _boardsState.SetState(boxName, ConnectionState.ReturningNonValues);
                }
            }

            void LowFrequencyLog(Action<string> logAction, string message)
            {
                if (timeSinceLastLog.IsRunning && timeSinceLastLog.ElapsedMilliseconds < 5 * 60000)
                {
                    if (logSkipped++ == 0)
                        LogError(board, $"{message}{Environment.NewLine}Skipping further messages for this board (max 2 messages every 5 minutes)");
                    return;
                }

                timeSinceLastLog.Restart();
                logAction(message);
                logSkipped = 0;
            }
        }

        ///<returns>(<c>false</c> if the board was explicitely detected as disconnected, the line, or null/default string if it exceeded the MCUBoard.ReadTimeout).</returns>
        ///<remarks>
        ///Notifies in _localBoardsState (which is reported to the next vector) if there is no data available within <param cref="msBetweenReads" /> 
        ///and when that happens it waits up to MCUBoard.ReadTimeout for the board to return data.
        ///Both in the above case and when the MCUBoard.ReadTimeout is exceeded a message is written to the log (but not the console to reduce operational noise),
        ///specially as it can now be observed on the graphs if board these events are happening + CheckFailure & reconnects will display relevant messages if appropiate.
        ///</remarks>
        private async Task<(bool, string?)> TryReadLineWithStallDetection(MCUBoard board, string boxName, int msBetweenReads, CancellationToken token)
        {
            var readLineTask = board.SafeReadLine(token);
            var noDataAvailableTask = Task.Delay(msBetweenReads, token); 
            if (await Task.WhenAny(readLineTask, noDataAvailableTask) == noDataAvailableTask)
            {
                LogData(board, "no data available");
                _boardsState.SetState(boxName, ConnectionState.NoDataAvailable); //report the state early before waiting up to 2 seconds for the data (readLineTask)
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
        protected virtual List<double>? TryParseAsDoubleList(MCUBoard board, string line) => 
            board.ConfigSettings.Parser.TryParseAsDoubleList(line);

        private bool CheckFails(MCUBoard board, SensorSample.InputBased[] values)
        {
            bool hasStaleValues = false;
            foreach (var item in values)
            {
                var msSinceLastRead = DateTime.UtcNow.Subtract(item.TimeStamp).TotalMilliseconds;
                if (msSinceLastRead <= board.ConfigSettings.MaxMillisecondsWithoutNewValues)
                    continue;
                hasStaleValues = true; 
                LogInfo(board, $"stale sensor detected: {item.Input.Name}. {msSinceLastRead} milliseconds since last read");
            }

            return hasStaleValues;
        }

        private async Task ReconnectBoard(MCUBoard board, string boxName, CancellationToken token)
        {
            _boardsState.SetAttemptingReconnectState(boxName);
            LogInfo(board, "attempting to reconnect");
            var lostSensorAttempts = 100;
            var delayBetweenAttempts = TimeSpan.FromSeconds(board.ConfigSettings.SecondsBetweenReopens);
            while (!(await board.SafeReopen(token)))
            {
                _boardsState.SetDisconnectedState(boxName);
                if (ExactSensorAttemptsCheck(ref lostSensorAttempts)) 
                { // we run this once when there has been 100 attempts
                    LogError(board, "reconnect limit exceeded, reducing reconnect frequency to 15 minutes");
                    delayBetweenAttempts = TimeSpan.FromMinutes(15); // 4 times x hour = 96 times x day
                }

                await Task.Delay(delayBetweenAttempts, token);
            }

            _boardsState.SetConnectedState(boxName);
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

        public virtual void ProcessLine(IEnumerable<double> numbers, MCUBoard board, SensorSample.InputBased[] targetSamples)
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

        private void DetectAndWarnSensorDisconnects(MCUBoard board, SensorSample.InputBased sensor)
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
        private SensorSample.InputBased[] GetSamples(MCUBoard board)
        {
            if (_boardSamplesLookup.TryGetValue(board, out var samples))
                return samples;
            _boardSamplesLookup[board] = samples = _values.Where(s => s.Input.BoxName == board.BoxName).ToArray();
            return samples;
        }

        private void LogError(IOconfMap board, string message, Exception ex) 
        {
            if (board.McuBoard != null)
                LogError(board.McuBoard, message, ex);
            else
                CALog.LogErrorAndConsoleLn(LogID.A, $"{message} - {Title} - {board.BoxName} (missing)", ex); 
        }
        private void LogError(MCUBoard board, string message, Exception ex) => CALog.LogErrorAndConsoleLn(LogID.A, $"{message} - {Title} - {board.ToShortDescription()}", ex);
        private void LogError(MCUBoard board, string message) => CALog.LogErrorAndConsoleLn(LogID.A, $"{message} - {Title} - {board.ToShortDescription()}");
        private void LogData(MCUBoard board, string message) => CALog.LogData(LogID.B, $"{message} - {Title} - {board.ToShortDescription()}");
        private void LogInfo(MCUBoard board, string message) => CALog.LogInfoAndConsoleLn(LogID.B, $"{message} - {Title} - {board.ToShortDescription()}");

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
                    _states[i] = _boardList[i].McuBoard?.InitialConnectionSucceeded == true ? ConnectionState.Connected : ConnectionState.Disconnected;
                }
            }

            public IEnumerator<(string, ConnectionState)> GetEnumerator() 
            {
                for (int i = 0; i < _sensorNames.Length; i++)
                    yield return (_sensorNames[i], _states[i]);
            }

            public void SetReadSensorsExceptionState(string boxName) => SetState(boxName, ConnectionState.ReadError);
            public void SetAttemptingReconnectState(string boxName) => SetState(boxName, ConnectionState.Connecting);
            public void SetDisconnectedState(string boxName) => SetState(boxName, ConnectionState.Disconnected);
            public void SetDisconnectedState(IOconfMap board) => SetState(board, ConnectionState.Disconnected);
            public void SetConnectedState(string boxName) => SetState(boxName, ConnectionState.Connected);
            public void SetState(IOconfMap board, ConnectionState state) => SetState(board.Name, state);
            public void SetState(string boxName, ConnectionState state) => _states[_boardsIndexes[boxName]] = state;

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
