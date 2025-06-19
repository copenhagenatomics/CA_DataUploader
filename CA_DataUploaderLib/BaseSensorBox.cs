#nullable enable
using CA_DataUploaderLib.Extensions;
using CA_DataUploaderLib.IOconf;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

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
        private readonly (IOconfMap map, SensorSample.InputBased[] values, DataVectorReader? vectorReader, int boardStateIndexInFullVector)[] _boards = [];
        protected readonly AllBoardsState _boardsState = new([]);
        private readonly Dictionary<MCUBoard, SensorSample.InputBased[]> _boardSamplesLookup = [];
        private readonly string mainSubsystem;
        private readonly Dictionary<MCUBoard, List<(Func<DataVector?, MCUBoard, CancellationToken, Task> write, Func<MCUBoard, CancellationToken, Task> exit)>> _builtInWriteActions = [];
        private readonly Dictionary<MCUBoard, (ChannelReader<string> Reader, ChannelWriter<string> Writer)> _boardCustomCommands = [];
        private static readonly Dictionary<CommandHandler, Dictionary<string, string>> _usedBoxNames = []; //Dictionary of used board names tied to a specific CommandHandler-instance
        private readonly Dictionary<string, TaskCompletionSource> _reconnectTasks = [];

        public BaseSensorBox(CommandHandler cmd, string commandName, IEnumerable<IOconfInput> values)
        {
            mainSubsystem = commandName.ToLower();
            Title = commandName;
            _cmd = cmd;
            _values = values.Select(x => new SensorSample.InputBased(x)).ToList();
            _localValues = _values.Where(x => x.Input.Map.IsLocalBoard).ToList();
            if (_values.Count == 0)
                return;  // no data

            SubscribeCommandsToSubsystems(cmd, mainSubsystem, _values);
            cmd.AddSubsystem(this);
            cmd.FullVectorIndexesCreated += InitializeBuiltInActionsIndexesAndVectorsChannel;

            var allBoards = _values.Where(x => !x.Input.Skip).GroupBy(x => x.Input.Map).Select(g => (map: g.Key, values: g.ToArray(), boardStateIndexInFullVector: -1)).ToArray();
            foreach (var board in allBoards)
            {
                RegisterReconnectBoardCommand(board.map, cmd, _reconnectTasks);
                RegisterCustomBoardCommand(board.map, cmd, _boardCustomCommands);
                EnforceBoardNotAlreadyInUse(board.map.BoxName, board.values.First().Input.Row, cmd);//used by another sensor type / BaseSensorBox instance
                EnforceNoDuplicatePorts(board.map.BoxName, board.values);
            }

            _boards = allBoards.Where(b => b.map.IsLocalBoard).Select(b => (b.map, b.values, vectorReader: (DataVectorReader?)null, boardStateIndexInFullVector: -1)).ToArray();
            _boardsState = new AllBoardsState(_boards.Select(b => b.map));


            static void EnforceNoDuplicatePorts(string boxName, SensorSample.InputBased[] sensors)
            {
                var duplicate = sensors.GroupBy(v => v.Input.PortNumber).FirstOrDefault(g => g.Count() > 1);
                if (duplicate == null) return;
                var dupSensors = string.Join(Environment.NewLine, duplicate.Select(s => s.Input.Row));
                throw new FormatException($"Can't map the same board port to different IO.conf lines: {boxName};{duplicate.Key}{Environment.NewLine}{dupSensors}");
            }
            static void EnforceBoardNotAlreadyInUse(string boxName, string newRow, CommandHandler cmd)
            {
                if (!_usedBoxNames.ContainsKey(cmd))
                {
                    _usedBoxNames[cmd] = [];
                    cmd.StopToken.Register(() => _usedBoxNames.Remove(cmd));
                }

                if (_usedBoxNames[cmd].TryGetValue(boxName, out var usedRow))
                    throw new FormatException($"Can't map the same board to different IO.conf line types: {boxName}{Environment.NewLine}{usedRow}{Environment.NewLine}{newRow}");
                _usedBoxNames[cmd].Add(boxName, newRow);
            }
            void RegisterCustomBoardCommand(IOconfMap map, CommandHandler cmd, Dictionary<MCUBoard, (ChannelReader<string> reader, ChannelWriter<string> writer)> boardCustomCommands)
            {
                if (!map.IsLocalBoard)
                {//if the board is not local we register the command validation with an empty action
                    cmd.AddMultinodeCommand("custom", ValidateCommand, _ => { });
                    return;
                }

                if (map.McuBoard == null)
                {
                    if (map.CustomWritesEnabled)
                        _cmd.Logger.LogData(LogID.A, $"Missing local board detected with custom writes enabled: {map.BoxName}"); //the missing board will be already reported to the user later.
                    cmd.AddMultinodeCommand("custom", ValidateCommand, _ => { _cmd.Logger.LogData(LogID.A, $"Custom command failed due to missing board: {map.BoxName}"); });
                    return;
                }

                //for local boards we instead register a multinode that sends the command to a local channel the write loop uses
                var channel = Channel.CreateUnbounded<string>();
                var channelWriter = channel.Writer; 
                boardCustomCommands.Add(map.McuBoard, (channel.Reader, channelWriter));
                cmd.AddMultinodeCommand("custom", ValidateCommand, a => channelWriter.TryWrite(string.Join(' ', a.Skip(2))));

                bool ValidateCommand(List<string> a)
                {
                    if (a.Count < 3 || a[1] != map.BoxName)
                        return false;

                    if (map.CustomWritesEnabled)
                        return true;

                    _cmd.Logger.LogError(LogID.A, $"Custom writes is not enabled for board: {map.BoxName}");
                    return false;
                }
            }
            void RegisterReconnectBoardCommand(IOconfMap map, CommandHandler cmd, Dictionary<string, TaskCompletionSource> reconnectTasks)
            {
                if (!map.IsLocalBoard)
                {//if the board is not local we register the command validation with an empty action
                    cmd.AddMultinodeCommand("reconnect", a => a.Count == 2 && a[1] == map.BoxName, _ => { });
                    return;
                }

                if (map.McuBoard == null)
                {
                    cmd.AddMultinodeCommand(
                        "reconnect", a => a.Count == 2 && a[1] == map.BoxName,
                        _ => { _cmd.Logger.LogData(LogID.A, $"Reconnect failed due to missing board: {map.BoxName}"); });
                    return;
                }

                reconnectTasks[map.BoxName] = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                cmd.AddMultinodeCommand(
                    "reconnect",
                    a => a.Count == 2 && a[1] == map.BoxName,
                    a =>
                    {
                        lock (reconnectTasks)
                        {
                            var currentTCS = reconnectTasks[map.BoxName];
                            reconnectTasks[map.BoxName] = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                            currentTCS.TrySetResult(); //Completes the task signaling to ReconnectBoard to try again (if currently waiting)
                        }
                    });
            }
        }

        private void InitializeBuiltInActionsIndexesAndVectorsChannel(object? _, IReadOnlyDictionary<string, int> indexes)
        {
            for (int i = 0; i < _boards.Length; i++)
            {
                var (map, values, _, _) = _boards[i];
                string name = GetBoxStateName(map.BoxName);
                if (!indexes.TryGetValue(name, out var index)) throw new ArgumentException($"Failed to find box state in full vector: {name}");
                var vectorReader = map.McuBoard != null && _builtInWriteActions.TryGetValue(map.McuBoard, out var _) ? _cmd.GetReceivedVectorsReader(1) : null;
                _boards[i] = (map, values, vectorReader, index);
            }
        }

        public Task Run(CancellationToken token) => RunBoardLoops(_boards, token);
        private void SubscribeCommandsToSubsystems(CommandHandler cmd, string mainSubsystem, List<SensorSample.InputBased> values)
        {
            cmd.AddMultinodeCommand(mainSubsystem, _ => true, ShowQueue);
            var subsystemOverrides = values.Select(v => v.Input.SubsystemOverride).OfType<string>().Distinct();
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
                 .Concat(GetBoards(values).Select(b => new VectorDescriptionItem("double", GetBoxStateName(b.BoxName), DataTypeEnum.State)))
                 .ToList();
            static IEnumerable<IOconfMap> GetBoards(IEnumerable<SensorSample.InputBased> n) =>
                n.Where(v => !v.Input.Skip).GroupBy(v => v.Input.Map).Select(b => b.Key);
        }

        public static string GetBoxStateName(string boxName) => boxName + "_state";

        /// <remarks>must be called before <see cref="CommandHandler.FullVectorDescriptionCreated"/> and <see cref="Run"/> are called</remarks>
        public void AddBuiltInWriteAction(MCUBoard board, Func<DataVector?, MCUBoard, CancellationToken, Task> writeAction, Func<MCUBoard, CancellationToken, Task> exitAction)
        {
            if (!_builtInWriteActions.TryGetValue(board, out var actions)) _builtInWriteActions[board] = [(writeAction, exitAction)];
            else actions.Add((writeAction, exitAction));
        }

        protected void RegisterBoardWriteActions(MCUBoard board, IOconfOutput port, double defaultTarget, IEnumerable<string> targetFieldNames, Func<int, IEnumerable<double>, string> getCommand, int repeatMilliseconds = 1000)
        {
            IEnumerable<int> fieldIndices = [];
            _cmd.FullVectorIndexesCreated += InitializeAction;
            RegisterBoardWriteActions(board, port, defaultTarget, GetIndices, getCommand, repeatMilliseconds);

            void InitializeAction(object? sender, IReadOnlyDictionary<string, int> indexes) =>
                fieldIndices = targetFieldNames.Select(n => indexes.TryGetValue(n, out var index)
                    ? index
                    : throw new InvalidOperationException($"Missing target field: {n}"));
            IEnumerable<int> GetIndices() => fieldIndices;
        }

        protected void RegisterBoardWriteActions(MCUBoard board, IOconfOutput port, double defaultTarget, Func<IEnumerable<int>> getIndices, Func<int, IEnumerable<double>, string> getCommand, int repeatMilliseconds = 1000)
        {
            List<double>? defaultTargets = null;
            IEnumerable<int>? vectorIndices = null;
            LastAction? lastAction = null;
            AddBuiltInWriteAction(board, WriteAction, ExitAction);

            Task ExitAction(MCUBoard board, CancellationToken token) => Off(board, port, token);
            async Task WriteAction(DataVector? vector, MCUBoard board, CancellationToken token)
            {
                vectorIndices ??= getIndices();
                lastAction ??= new LastAction(vectorIndices, repeatMilliseconds);
                defaultTargets ??= vectorIndices.Select(i => defaultTarget).ToList();

                if (vector == null)
                {
                    await board.SafeWriteLine(getCommand(port.PortNumber, defaultTargets), token);
                    lastAction.TimedOutWaitingForDecision();
                    return;
                }

                if (!lastAction.ChangedOrExpired(vector.Data, vector.Timestamp))
                    return;

                lastAction.ExecutedNewAction(vector.Data, vector.Timestamp);
                await board.SafeWriteLine(getCommand(port.PortNumber, lastAction.Targets), token);
            }

            async Task Off(MCUBoard board, IOconfOutput port, CancellationToken token)
            {
                vectorIndices ??= getIndices();
                defaultTargets ??= vectorIndices.Select(i => defaultTarget).ToList();

                string msg = getCommand(port.PortNumber, defaultTargets);
                await board.SafeWriteLine(msg, token);
                _cmd.Logger.LogInfo(LogID.A, $"Port has been set to default position ({string.Join(", ", defaultTargets.Select(t => $"{t:F2}"))}): {port.Name}. Command: {msg}");
            }
        }

        private void ShowQueue(List<string> args)
        {
            var subsystem = args[0].ToLower();
            var isMainCommand = subsystem == mainSubsystem;
            StringBuilder sb = new();
            foreach (var t in _localValues)
            {
                string? subsystemOverride = t.Input.SubsystemOverride;
                var matchesSubsystem = (isMainCommand && subsystemOverride == default) || subsystemOverride == subsystem;
                if (matchesSubsystem)
                    sb.AppendLine($"{t.Input.Name,-22}={t.Value,9:N2}");
            }

            _cmd.Logger.LogInfo(LogID.A, sb.ToString());
        }

        private async Task RunBoardLoops((IOconfMap map, SensorSample.InputBased[] values, DataVectorReader? vectorReader, int boardStateIndexInFullVector)[] boards, CancellationToken token)
        {
            long start = _cmd.Time.GetTimestamp();
            try
            {
                var loops = StartLoops(boards, token);
                await Task.WhenAll(loops);
                if (loops.Count > 0) //we only report the exit when we actually ran loops with detected boards. If a board was not detected StartReadLoops already reports the missing boards.
                    _cmd.Logger.LogInfo(LogID.A, $"Exiting {Title}.RunBoardLoops() " + _cmd.Time.GetElapsedTime(start));
            }
            catch (Exception ex)
            {
                _cmd.Logger.LogError(LogID.A, $"{Title} - unexpected error detected", ex);
            }

            Stopping?.Invoke(this, EventArgs.Empty);
            foreach (var (map, _, _, _) in boards)
            {
                try
                {
                    using var closeTimeoutToken = new CancellationTokenSource(TimeSpan.FromSeconds(5), _cmd.Time);
                    map.McuBoard?.SafeClose(closeTimeoutToken.Token);
                    _boardsState.SetDisconnectedState(map);
                }
                catch (Exception ex)
                {
                    LogError(map, "Error closing the connection to the board", ex);
                }
            }
        }

        protected virtual List<Task> StartLoops((IOconfMap map, SensorSample.InputBased[] values, DataVectorReader? vectorReader, int boardStateIndexInFullVector)[] boards, CancellationToken token)
        {
            var missingBoards = boards.Where(h => h.map.McuBoard == null).Select(b => b.map.BoxName).Distinct().ToList();
            if (missingBoards.Count > 0)
                _cmd.Logger.LogError(LogID.A, $"{Title} - missing boards detected {string.Join(",", missingBoards)}. Related sensors/actuators are disabled.");

            return boards
                .Where(b => b.map.McuBoard != null) //we ignore the missing boards for now as we don't have auto reconnect logic yet for boards not detected during system start.
                .SelectMany(b => new[] { BoardReadLoop(b.map.McuBoard!, b.map.BoxName, b.values, token), BoardWriteLoop(b.map.McuBoard!, b.vectorReader, b.boardStateIndexInFullVector, token) })
                .ToList();
        }

        private async Task BoardWriteLoop(MCUBoard board, DataVectorReader? vectorReader, int boardStateIndexInFullVector, CancellationToken token)
        {
            var customWritesEnabled = _boardCustomCommands.TryGetValue(board, out var customCommandsChannel);
            var builtInActionsEnabled = _builtInWriteActions.TryGetValue(board, out var builtInActions);
            if (!builtInActionsEnabled && !customWritesEnabled) return;
            DataVectorReader? vectorsChannel = builtInActionsEnabled
                ? (vectorReader ?? throw new InvalidOperationException("Built-in actions detected without receiving vectors channel being initialized"))
                : null;

            // we use the next 2 variables to avoid spamming logs/display with an ongoing problem, so we only notify at the beginning and when we resume normal operation.
            // we might still get lots of entries for problems that alternate between normal and failed states, but for now is a good data point to know if that case is happening.
            var waitingBoardReconnect = false;
            long timeSinceTimeoutsStarted = 0;

            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (customWritesEnabled && customCommandsChannel!.Reader.TryRead(out var command))
                        await board.SafeWriteLine(command, token);

                    if (!builtInActionsEnabled)
                    {
                        EnsureResumeAfterTimeoutIsReported();
                        await customCommandsChannel!.Reader.WaitToReadAsync(token);
                        continue;
                    }

                    DataVector? vector = null;
                    do
                    {
                        vector = await vectorsChannel!.ReadWithSoftTimeout(2000, _cmd.Time, token);
                        token.ThrowIfCancellationRequested(); //we are stopping, let's break of the top loop so the stop actions run
                        if (vector != null && !CheckConnectedStateInVector(board, boardStateIndexInFullVector, ref waitingBoardReconnect, vector))
                            continue; // no point trying to send commands while there is no connection to the board.

                        foreach (var (writeAction, _) in builtInActions!)
                            await writeAction(vector, board, token);

                        EnsureResumeAfterTimeoutIsReported();
                    }
                    while (vector == null);
                }
                catch (TimeoutException)
                {
                    EnsureTimeoutIsReportedOnce();
                    await Task.WhenAny(Task.Delay(TimeSpan.FromMilliseconds(500), _cmd.Time, token)); //reduce action frequency / the WhenAny avoids exceptions if the token is canceled
                }
                catch (OperationCanceledException ex) when (ex.CancellationToken == token)
                { }
                catch (ObjectDisposedException)
                {
                    LogData(board, "Detected closed connection (write loop)");
                    return;
                }
                catch (Exception ex)
                {
                    LogError(board, "Error detected writing to board", ex);
                }
            }

            if (!builtInActionsEnabled) return;

            try
            {
                foreach (var (_, exitAction) in builtInActions!)
                {
                    using var timeoutToken = new CancellationTokenSource(TimeSpan.FromSeconds(3), _cmd.Time);
                    await exitAction(board, timeoutToken.Token);
                }
            }
            catch (Exception ex)
            {
                LogError(board, "Error running exit actions for board", ex);
            }

            void EnsureTimeoutIsReportedOnce()
            {
                if (timeSinceTimeoutsStarted != 0) return;
                LogInfo(board, $"Timed out writing to board, reducing action frequency until reconnect");
                timeSinceTimeoutsStarted = _cmd.Time.GetTimestamp();
            }

            void EnsureResumeAfterTimeoutIsReported()
            {
                if (timeSinceTimeoutsStarted == 0) return;
                LogInfo(board, $"Wrote to board without time outs after {_cmd.Time.GetElapsedTime(timeSinceTimeoutsStarted)}, resuming normal action frequency");
                timeSinceTimeoutsStarted = 0;
            }

            bool CheckConnectedStateInVector(MCUBoard board, int boardState, ref bool waitingBoardReconnect, DataVector vector)
            {
                var vectorState = (ConnectionState)(int)vector[boardState];
                var connected = vectorState >= ConnectionState.Connected;
                if (waitingBoardReconnect && connected)
                {
                    LogData(board, $"Resuming writes after reconnect");
                    waitingBoardReconnect = false;
                }
                else if (!waitingBoardReconnect && !connected)
                {
                    LogData(board, $"Stopping writes until connection is reestablished (state: {vectorState})");
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
                    await ReadSensors(board, boxName, targetSamples, token);
                    _boardsState.SetDisconnectedState(boxName);
                    if (board.Closed)
                        return;
                    token.ThrowIfCancellationRequested();
                    await ReconnectBoard(board, boxName, token);
                }
                catch (OperationCanceledException ex)
                { //if the token is canceled we are about to exit the loop so we do nothing. Otherwise we consider it like any other exception and log it.
                    if (!token.IsCancellationRequested)
                    {
                        _boardsState.SetReadSensorsExceptionState(boxName);
                        LogError(board, "Unexpected error on board read loop", ex);
                    }
                }
                catch (Exception ex)
                { // we expect most errors to be handled within ReadSensors and in the SafeReopen of the ReconnectBoard,
                  // so seeing this in the log is most likely a bug handling some error case.
                    _boardsState.SetReadSensorsExceptionState(boxName);
                    LogError(board, "Unexpected error on board read loop", ex);
                }
            }
        }
        
        protected void RunCustomCommand(MCUBoard board, string command)
        {
            var (_, channelWriter) = _boardCustomCommands[board];
            channelWriter.TryWrite(command);
            return;
        }

        ///<returns><c>false</c> if a board disconnect was detected</returns>
        private async Task ReadSensors(MCUBoard board, string boxName, SensorSample.InputBased[] targetSamples, CancellationToken token)
        {
            var lastValidReadTime = _cmd.Time.GetTimestamp();
            // we need to allow some extra time to avoid too aggressive reporting of boards not giving data, no particular reason for it being 50%.
            var timeBetweenReads = TimeSpan.FromMilliseconds(board.ConfigSettings.MillisecondsBetweenReads * 1.5);
            long lastLogInfoTime = 0, lastLogErrorTime = 0, lastLogBoardErrorTime = 0, lastLogBoardOkTime = 0, lastMultilineMessageTime = 0, lastHighResolutionErrorTime = 0;
            int logInfoSkipped = 0, logErrorSkipped = 0, logBoardErrorSkipped = 0, logBoardOkSkipped = 0, multilineMessageSkipped = 0, highResolutionErrorSkipped = 0;
            uint lastStatus = 0;
            bool highResolutionMode = false;
            Lazy<HighResolutionWriter> highResolutionWriter = new(new HighResolutionWriter(Path.Combine("..", "recordings"), boxName, (message) => LowFrequencyHighResolutionError((args, skipMessage) => LogError(args.board, $"{args.message}{skipMessage}"), (board, message))));
            MultilineMessageReceiver multilineMessageReceiver = new((message) => LowFrequencyMultilineMessage((args, skipMessage) => LogInfo(args.board, $"{args.message}{skipMessage}"), (board, message)));
            //We set the state early if we detect no data is being returned or if we received values,
            //but we only set ReturningNonValues if it has passed timeBetweenReads since the last valid read
            while (!token.IsCancellationRequested) // we only stop reading if a disconnect/timeout is detected or the process is being stopped
            {
                var line = await TryReadLineWithStallDetection(board, boxName, timeBetweenReads, token);
                if (line == default)
                {
                    multilineMessageReceiver.LogPossibleIncompleteMessage();
                    if (highResolutionMode)
                        await highResolutionWriter.Value.StopAsync(token); 
                    return; //timed out/unexpected error reading from the board, let the caller run the reconnect flow
                }

                try
                {
                    if (multilineMessageReceiver.HandleLine(line))
                        continue;

                    var (numbers, status) = TryParseAsDoubleList(board, line);
                    if (numbers != null)
                    {
                        ProcessLine(numbers, board, targetSamples);
                        lastValidReadTime = _cmd.Time.GetTimestamp();
                        _boardsState.SetState(boxName, ConnectionState.ReceivingValues);

                        if (!highResolutionMode && (status & 0x02000000) != 0) // High resolution mode started?
                        {
                            highResolutionMode = true;
                            timeBetweenReads = TimeSpan.FromMilliseconds(10 * 1.5);
                            await highResolutionWriter.Value.WriteLineAsync(string.Join(", ", targetSamples.Select(s => s.Input.Name)), token);
                        }
                        if (highResolutionMode && (status & 0x02000000) == 0) // High resolution mode ended?
                        {
                            highResolutionMode = false;
                            timeBetweenReads = TimeSpan.FromMilliseconds(board.ConfigSettings.MillisecondsBetweenReads * 1.5);
                            await highResolutionWriter.Value.StopAsync(token);
                        }
                        if (highResolutionMode)
                            await highResolutionWriter.Value.WriteLineAsync(line, token);

                        if (status != lastStatus && (status & 0x01000000) != 0) // Flash ongoing?
                            timeBetweenReads = TimeSpan.FromSeconds(1.5);
                        if ((status & 0x01000000) == 0 && (lastStatus & 0x01000000) != 0) // Flash done?
                            timeBetweenReads = TimeSpan.FromMilliseconds(board.ConfigSettings.MillisecondsBetweenReads * 1.5);
                        if ((status & 0x80000000) != 0 && (lastStatus & 0x80000000) == 0) // Error?
                        {
                            LowFrequencyLogBoardError((args, skipMessage) =>
                            {
                                LogError(args.board, $"Board entered error state with 0x{args.status:X}{skipMessage}");
                                if (_boardCustomCommands.TryGetValue(board, out var customCommandsChannel))
                                    customCommandsChannel.Writer.TryWrite("Status");
                            }, (board, status));
                        }
                        if ((status & 0x80000000) == 0 && (lastStatus & 0x80000000) != 0) // Error gone?
                        {
                            LowFrequencyLogBoardOk((args, skipMessage) => LogInfo(args.board, $"Board resumed normal state with 0x{args.status:X}{skipMessage}"), (board, status));
                        }
                        lastStatus = status;
                    }
                    else if (!board.ConfigSettings.Parser.IsExpectedNonValuesLine(line))// mostly responses to commands or headers on reconnects.
                    {
                        LowFrequencyLogInfo((args, skipMessage) => LogInfo(args.board, $"Unexpected board response '{args.line.ToLiteral()}'{skipMessage}"), (board, line));
                        if (_cmd.Time.GetElapsedTime(lastValidReadTime) > timeBetweenReads)
                            _boardsState.SetState(boxName, ConnectionState.ReturningNonValues);
                    }
                }
                catch (Exception ex)
                { //usually a parsing errors on non value data, we log it and consider it as such i.e. we set ReturningNonValues if we have not had a valid read in timeBetweenReads
                    LowFrequencyLogError((args, skipMessage) => LogError(args.board, $"Failed handling board response '{args.line.ToLiteral()}'{skipMessage}", args.ex), (board, line, ex));
                    if (_cmd.Time.GetElapsedTime(lastValidReadTime) > timeBetweenReads)
                        _boardsState.SetState(boxName, ConnectionState.ReturningNonValues);
                }
            }

            void LowFrequencyLogInfo<T>(Action<T, string> logAction, T args) => LowFrequencyLog(logAction, "info", args, ref lastLogInfoTime, ref logInfoSkipped);
            void LowFrequencyLogError<T>(Action<T, string> logAction, T args) => LowFrequencyLog(logAction, "error", args, ref lastLogErrorTime, ref logErrorSkipped);
            void LowFrequencyLogBoardError<T>(Action<T, string> logAction, T args) => LowFrequencyLog(logAction, "board error", args, ref lastLogBoardErrorTime, ref logBoardErrorSkipped);
            void LowFrequencyLogBoardOk<T>(Action<T, string> logAction, T args) => LowFrequencyLog(logAction, "board ok", args, ref lastLogBoardOkTime, ref logBoardOkSkipped);
            void LowFrequencyMultilineMessage<T>(Action<T, string> logAction, T args) => LowFrequencyLog(logAction, "multiline", args, ref lastMultilineMessageTime, ref multilineMessageSkipped);
            void LowFrequencyHighResolutionError<T>(Action<T, string> logAction, T args) => LowFrequencyLog(logAction, "high resolution", args, ref lastHighResolutionErrorTime, ref highResolutionErrorSkipped);

            void LowFrequencyLog<T>(Action<T, string> logAction, string logType, T args, ref long lastLogTime, ref int logSkipped)
            {
                if (lastLogTime != 0 && _cmd.Time.GetElapsedTime(lastLogTime).TotalMinutes < 5)
                {
                    if (logSkipped++ == 0)
                        logAction(args, $"{Environment.NewLine}Skipping further messages for this board (max 2 {logType} messages every 5 minutes)");
                    return;
                }

                lastLogTime = _cmd.Time.GetTimestamp();
                logAction(args, "");
                logSkipped = 0;
            }
        }

        ///<returns>(<c>false</c> if the board was explicitly detected as disconnected, the line, or null/default string if it exceeded the MCUBoard.ReadTimeout).</returns>
        ///<remarks>
        ///Notifies in _localBoardsState (which is reported to the next vector) if there is no data available within <param cref="timeBetweenReads" /> 
        ///and when that happens it waits up to MCUBoard.ReadTimeout for the board to return data.
        ///If MCUBoard.ReadTimeout is exceeded a message is written to the event log, 
        ///but a regular disconnect only writes to the local log as reconnects will display relevant messages.
        ///</remarks>
        private async Task<string?> TryReadLineWithStallDetection(MCUBoard board, string boxName, TimeSpan timeBetweenReads, CancellationToken token)
        {
            var readLineTask = board.SafeReadLine(token);
            var noDataAvailableTask = Task.Delay(timeBetweenReads, _cmd.Time, token);
            long noDataDetectedTime = 0;
            if (await Task.WhenAny(readLineTask, noDataAvailableTask) == noDataAvailableTask && !token.IsCancellationRequested)
            {
                LogData(board, $"No data available ({readLineTask.IsCompleted})");
                noDataDetectedTime = _cmd.Time.GetTimestamp();
                _boardsState.SetState(boxName, ConnectionState.NoDataAvailable); //report the state early before waiting up to 2 seconds for the data (readLineTask)
            }

            try
            {
                var line = await readLineTask; // waits up to 2 seconds for the read to complete, while we are here the state keeps being no data.
                if (noDataDetectedTime != 0)
                    LogData(board, $"Time from no data available to data available: {_cmd.Time.GetElapsedTime(noDataDetectedTime).TotalMilliseconds}ms");
                return line;
            }
            catch (OperationCanceledException ex) when (ex.CancellationToken == token) 
            {
                throw;
            }
            catch (ObjectDisposedException)
            {
                LogData(board, "Detected closed connection");
                return default;
            }
            catch (ArgumentOutOfRangeException ex)
            {
                //typically readline reports ObjectDisposedException once when disconnecting a temp hub and later calls fail with "ArgumentOutOfRangeException: Non-negative number required."
                //calling code is expected to handle the disconnected board returned by ObjectDisposedException so this is logged and displayed as an error.
                //more testing is needed with switchboards, as potentially a write could get the ObjectDisposeException which might end up hitting this catch statement.
                LogError(board, "Detected closed connection", ex);
                return default;
            }
            catch (TimeoutException)
            {
                LogInfo(board, "Timed out reading data");
                return default;
            }
            catch (Exception ex)
            {
                //typically readline reports the earlier exception types, so we log an error to the event log for visibility in this case
                LogError(board, "Error reading sensor data", ex);
                return default;
            }
        }

        /// <returns>the list of doubles (<c>null</c> if failing to parse the line) and a status value</returns>
        protected virtual (List<double>?, uint) TryParseAsDoubleList(MCUBoard board, string line) =>
            board.ConfigSettings.Parser.TryParseAsDoubleList(line);

        private async Task ReconnectBoard(MCUBoard board, string boxName, CancellationToken token)
        {
            _boardsState.SetAttemptingReconnectState(boxName);
            LogInfo(board, "Attempting to reconnect");
            var lostSensorAttempts = 100;
            var delayBetweenAttempts = TimeSpan.FromSeconds(board.ConfigSettings.SecondsBetweenReopens);
            var (reconnected, reconnectResponse) = await board.SafeReopen(token);
            while (!reconnected)
            {
                _boardsState.SetDisconnectedState(boxName);
                if (ExactSensorAttemptsCheck(ref lostSensorAttempts))
                { // we run this once when there has been 100 attempts
                    LogError(board, "Reconnect limit exceeded, reducing reconnect frequency to 15 minutes");
                    delayBetweenAttempts = TimeSpan.FromMinutes(15); // 4 times x hour = 96 times x day
                }

                Task reconnectTask;
                lock (_reconnectTasks)
                    reconnectTask = _reconnectTasks[boxName].Task;
                await Task.WhenAny(reconnectTask, Task.Delay(delayBetweenAttempts, _cmd.Time, token));
                token.ThrowIfCancellationRequested();
                _boardsState.SetAttemptingReconnectState(boxName);
                (reconnected, reconnectResponse) = await board.SafeReopen(token);
            }

            _boardsState.SetConnectedState(boxName);
            LogInfo(board, $"Board reconnection succeeded{Environment.NewLine}Board response: {reconnectResponse}");
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
                LogError(board, $"Sensor {sensor.Name} has been unreachable for at least 5 minutes (returning 10k+ values)");

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
                _cmd.Logger.LogError(LogID.A, $"{message} - {Title} - {board.BoxName} (missing)", ex);
        }
        private void LogError(Board board, string message, Exception ex) => _cmd.Logger.LogError(LogID.A, $"{message} - {Title} - {board.ToShortDescription()}", ex);
        private void LogError(Board board, string message) => _cmd.Logger.LogError(LogID.A, $"{message} - {Title} - {board.ToShortDescription()}");
        private void LogData(Board board, string message) => _cmd.Logger.LogData(LogID.B, $"{message} - {Title} - {board.ToShortDescription()}");
        private void LogInfo(Board board, string message) => _cmd.Logger.LogInfo(LogID.B, $"{message} - {Title} - {board.ToShortDescription()}");


        /// <summary>
        /// Receives and logs lines part of a multiline message delimited by "Start of" and "End of".
        /// </summary>
        public class MultilineMessageReceiver(Action<string> log)
        {
            private StringBuilder multilineMessage = new();
            private bool multilineMessageMode = false;
            private int multilineMessageLineCount = 0;
            private const int maxMultilineMessageLineCount = 30;
            private const string startTag = "Start of";
            private const string endTag = "End of";

            /// <summary>
            /// Detects and logs multiline messages.
            /// </summary>
            /// <param name="line"></param>
            /// <returns>True, if the line is part of a multiline message and should be considered handled.</returns>
            public bool HandleLine(string line)
            {
                line = line.Trim();
                if (line.StartsWith(startTag, StringComparison.InvariantCultureIgnoreCase) || multilineMessageMode)
                {
                    multilineMessageMode = true;
                    multilineMessage.AppendLine(line);
                    if (line.StartsWith(endTag, StringComparison.InvariantCultureIgnoreCase) ||
                        multilineMessageLineCount++ >= maxMultilineMessageLineCount)
                    {
                        LogMessage();
                    }
                    return true;
                }
                return false;
            }

            /// <summary>
            /// Logs any incomplete multiline message.
            /// </summary>
            public void LogPossibleIncompleteMessage()
            {
                if (!multilineMessageMode)
                    return;

                multilineMessage.AppendLine("*Incomplete*");
                LogMessage();
            }

            private void LogMessage()
            {
                log($"{multilineMessage}");
                multilineMessage = new();
                multilineMessageMode = false;
                multilineMessageLineCount = 0;
            }
        }

        public class HighResolutionWriter
        {
            public const int MaxFilesInFolder = 20; // Maximum number of high resolution files to keep in the folder
            private readonly MemoryStream stream;
            private readonly StreamWriter writer;
            private readonly string path, name;
            private readonly Action<string> log;
            private readonly TimeProvider timeProvider;

            public HighResolutionWriter(string path, string name, Action<string> log, TimeProvider? timeProvider = null)
            {
                stream = new MemoryStream(1_000_000);
                writer = new StreamWriter(stream, Encoding.ASCII, 1_000);
                this.path = path;
                this.name = name;
                this.log = log;
                this.timeProvider = timeProvider ?? TimeProvider.System;
            }

            /// <summary>
            /// Stop accumulating data and write to file - if there are not too many files in the folder.
            /// </summary>
            public async Task StopAsync(CancellationToken token)
            {
                await writer.FlushAsync(token);
                if (stream.Position == 0)
                    return;
                if (Directory.GetFiles(path, $"HighResolution_{name}_*.zip", SearchOption.AllDirectories).Length < MaxFilesInFolder)
                {
                    stream.Position = 0; // Reset stream position for reading
                    var fileName = $"HighResolution_{name}_{timeProvider.GetUtcNow():yyyy-MM-dd_HH-mm-ss}";
                    using var fileStream = new FileStream(Path.Combine(path, fileName + ".zip"), FileMode.Create, FileAccess.Write);
                    using var archive = new ZipArchive(fileStream, ZipArchiveMode.Create);
                    var entry = archive.CreateEntry(fileName + ".csv");
                    using var entryStream = entry.Open();
                    stream.CopyTo(entryStream);
                }
                else
                    log.Invoke($"Skipping writing high resolution data for {name} as the folder has too many files (>{MaxFilesInFolder})");
                stream.SetLength(0);
            }

            public async Task WriteLineAsync(string line, CancellationToken token)
            {
                await writer.WriteLineAsync(line);
                if (stream.Length > 900_000) // Flush if the stream is getting too large
                    await StopAsync(token);
            }
        }

        public class AllBoardsState : IEnumerable<(string sensorName, ConnectionState State)>
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
                    _sensorNames[i] = GetBoxStateName(_boardList[i].BoxName);
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
            Connected = 2, // we have successfully connected to the board and will soon be attempting to read from it
            ReadError = 3, // there are unexpected exceptions communicating with the board
            NoDataAvailable = 4, // we are connected to the box, but we have not received for 150ms+
            ReturningNonValues = 5, // we are getting data from the box, but these are not values lines
            ReceivingValues = 6 // we are connected & receiving values
        }
    }
}
