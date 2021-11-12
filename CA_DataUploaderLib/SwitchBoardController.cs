using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CA.LoopControlPluginBase;
using CA_DataUploaderLib.Extensions;
using CA_DataUploaderLib.IOconf;
using Humanizer;

namespace CA_DataUploaderLib
{
    public sealed class SwitchBoardController : IDisposable, ISubsystemWithVectorData
    {
        private readonly BaseSensorBox _reader;
        private static readonly object ControllerInitializationLock = new object();
        private readonly PluginsCommandHandler _cmd;
        private readonly List<IOconfOut230Vac> _ports;
        private static SwitchBoardController _instance;
        private readonly TaskCompletionSource _boardControlLoopsStopped = new TaskCompletionSource();
        private readonly CancellationTokenSource _boardLoopsStopTokenSource = new CancellationTokenSource();

        public string Title => "SwitchboardActuation"; //not really displayed as this subsystem does not have data points

        private SwitchBoardController(CommandHandler cmd) 
        {
            _cmd = new PluginsCommandHandler(cmd);
            _ports = IOconfFile.GetEntries<IOconfOut230Vac>().Where(p => p.IsSwitchboardControllerOutput).ToList();
            var boardsTemperatures = _ports.GroupBy(p => p.BoxName).Select(b => b.Select(p => p.GetBoardTemperatureInputConf()).FirstOrDefault());
            var sensorPortsInputs = IOconfFile.GetEntries<IOconfSwitchboardSensor>().SelectMany(i => i.GetExpandedConf());
            var inputs = _ports.SelectMany(p => p.GetExpandedInputConf()).Concat(boardsTemperatures).Concat(sensorPortsInputs);
            //we register the subsystem before the reader auto registers, ensuring our Run is called first.
            //reason: if there are no boards connected, _reader.Stopping can be raised synchronously
            //        preventing our sub system from running and causing a deadlock as it WaitForLoopStopped.
            cmd.AddSubsystem(this); 
            _reader = new BaseSensorBox(cmd, "switchboards", string.Empty, "show switchboards inputs", inputs);
            _reader.Stopping += WaitForLoopStopped;
            cmd.AddCommand("escape", Stop);
        }

        public Task Run(CancellationToken token) => RunBoardControlLoops(_ports, token);
        public void Dispose() 
        { 
            _boardLoopsStopTokenSource.Cancel();
            _reader.Dispose();
        }

        public static SwitchBoardController GetOrCreate(CommandHandler cmd)
        {
            if (_instance != null) return _instance;
            lock (ControllerInitializationLock)
            {
                if (_instance != null) return _instance;
                return _instance = new SwitchBoardController(cmd);
            }
        }

        private bool Stop(List<string> args)
        {
            _boardLoopsStopTokenSource.Cancel();
            return true;
        }

        private async Task BoardLoop(MCUBoard board, List<IOconfOut230Vac> ports, CancellationToken token)
        {
            var lastActions = new SwitchboardAction[ports.Max(p => p.PortNumber)];
            var boardStateName = board.BoxName + "_state";
            // we use the next 2 booleans to avoid spamming logs/display with an ongoing problem, so we only notify at the beginning and when we resume normal operation.
            // we might still get lots of entries for problems that alternate between normal and failed states, but for now is a good data point to know if that case is happening.
            var waitingBoardReconnect = false;
            var tryingToRecoverAfterTimeoutWatch = new Stopwatch();
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var vector = await _cmd.When(_ => true, token);
                    if (!CheckConnectedStateInVector(board, boardStateName, ref waitingBoardReconnect, vector)) 
                        continue; // no point trying to send commands while there is no connection to the board.

                    foreach (var port in ports)
                        await DoPortActions(vector, board, port, lastActions, token);

                    if (tryingToRecoverAfterTimeoutWatch.IsRunning)
                    {
                        tryingToRecoverAfterTimeoutWatch.Stop();
                        CALog.LogInfoAndConsoleLn(LogID.A, $"wrote to switch board without time outs after {tryingToRecoverAfterTimeoutWatch.Elapsed}, resuming normal action frequency - {board.ToShortDescription()}");
                    }
                }
                catch (TimeoutException)
                {
                    if (!tryingToRecoverAfterTimeoutWatch.IsRunning) 
                    {
                        // we only notify of the situation once while this is a different way a disconnect might look like, we have
                        CALog.LogInfoAndConsoleLn(LogID.A, $"timed out writing to switchboard, reducing action frequency until reconnect - {board.ToShortDescription()}");
                        tryingToRecoverAfterTimeoutWatch.Restart();
                    }
                    // forcing reduced acting frequency )
                    try { await Task.Delay(500, token); } 
                    catch (TaskCanceledException) { }
                }
                catch (TaskCanceledException ex)
                {
                    if (!token.IsCancellationRequested)
                        CALog.LogErrorAndConsoleLn(LogID.A, ex.ToString());
                }
                catch (Exception ex)
                {
                    CALog.LogErrorAndConsoleLn(LogID.A, ex.ToString());
                }
            }

            AllOff(board, ports);
        }

        private static bool CheckConnectedStateInVector(MCUBoard board, string boardStateName, ref bool waitingBoardReconnect, NewVectorReceivedArgs vector)
        {
            var connected = (BaseSensorBox.ConnectionState)(int)vector[boardStateName] >= BaseSensorBox.ConnectionState.Connected;
            if (waitingBoardReconnect && connected)
            {
                CALog.LogInfoAndConsoleLn(LogID.A, $"resuming switchboard actions after reconnect on {board.ToShortDescription()}");
                waitingBoardReconnect = false;
            }
            else if (!waitingBoardReconnect && !connected)
            {
                CALog.LogInfoAndConsoleLn(LogID.A, $"stopping switchboard actions while connection is reestablished on {board.ToShortDescription()}");
                waitingBoardReconnect = true;
            }
            return connected;
        }

        private async Task RunBoardControlLoops(List<IOconfOut230Vac> ports, CancellationToken token)
        {
            DateTime start = DateTime.Now;
            try
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, _boardLoopsStopTokenSource.Token);
                //we ignore remote boards and boards missing during the start sequence (as we don't have auto reconnect logic yet for those). Note the reader in the ctor already reports the missing local boards.
                var connectedBoards = ports.Where(p => p.Map.IsLocalBoard && p.Map.Board != null);
                var boardLoops = connectedBoards
                    .GroupBy(v => v.Map.Board)
                    .Select(g => BoardLoop(g.Key, g.ToList(), linkedCts.Token))
                    .ToList();
                await Task.WhenAll(boardLoops);                
                CALog.LogInfoAndConsoleLn(LogID.A, "Exiting SwitchBoardController.RunBoardControlLoops() " + DateTime.Now.Subtract(start).Humanize(5));
            }
            catch (Exception ex)
            {
                CALog.LogErrorAndConsoleLn(LogID.A, ex.ToString());
            }
            finally
            {
                _boardControlLoopsStopped.TrySetResult();
            }
        }
        
        private static async Task DoPortActions(NewVectorReceivedArgs vector, MCUBoard board, IOconfOut230Vac port, SwitchboardAction[] lastActions, CancellationToken token)
        {
            if (token.IsCancellationRequested)
                return;
            try
            {
                var action = SwitchboardAction.FromVectorSamples(vector, port.Name);
                if (action.Equals(lastActions[port.PortNumber - 1]))
                    return; // no action changes has been requested since the last action taken on the heater.
                
                var onSeconds = action.GetRemainingOnSeconds(vector.GetVectorTime());
                if (onSeconds <= 0)
                    await board.SafeWriteLine($"p{port.PortNumber} off", token);
                else if (onSeconds == int.MaxValue)
                    await board.SafeWriteLine($"p{port.PortNumber} on", token);
                else
                    await board.SafeWriteLine($"p{port.PortNumber} on {onSeconds}", token);
                lastActions[port.PortNumber - 1] = action;
            }
            catch (TimeoutException)
            { 
                // we don't want logging at this level as the caller handles this in a way that reduces the amount of noise for failures that last many vectors.
                throw;
            }
            catch (Exception)
            {
                CALog.LogErrorAndConsoleLn(LogID.A, $"Failed executing port action for board {board.BoxName} port {port.Name} - {port.Name}");
                throw; // this will log extra info and avoid extra board actions on this cycle
            }
        }

        private static void AllOff(MCUBoard board, List<IOconfOut230Vac> ports)
        {
            try
            {
                board.SafeWriteLine("off", CancellationToken.None); 
                foreach (var port in ports)
                    if (port.HasOnSafeState)
                        board.SafeWriteLine($"p{port.PortNumber} on", CancellationToken.None);
            }
            catch (Exception ex)
            {
                CALog.LogErrorAndConsoleLn(LogID.A, $"Error detected while attempting to set ports to default positions for board {board.ToShortDescription()}", ex);
            }
        }

        private void WaitForLoopStopped(object sender, EventArgs args)
        {
            CALog.LogData(LogID.A, "waiting for switchboards control loops to finish actions and set ports to their default value");
            _boardControlLoopsStopped.Task.Wait();
            CALog.LogData(LogID.A, "finished waiting for switchboards loops");
        }

        public List<VectorDescriptionItem> GetLocalInputsDescriptionItems() => new();
        public List<VectorDescriptionItem> GetVectorDescriptionItems() => new();
        public IEnumerable<SensorSample> GetInputValues() => Enumerable.Empty<SensorSample>();
        public IEnumerable<SensorSample> GetDecisionOutputs(NewVectorReceivedArgs inputVectorReceivedArgs) => Enumerable.Empty<SensorSample>();
    }
}
