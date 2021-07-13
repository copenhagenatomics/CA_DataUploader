using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CA.LoopControlPluginBase;
using CA_DataUploaderLib.Extensions;
using CA_DataUploaderLib.IOconf;
using Humanizer;

namespace CA_DataUploaderLib
{
    public sealed class SwitchBoardController : IDisposable
    {
        /// <summary>runs when the subsystem is about to stop running, but before all boards are closed</summary>
        /// <remarks>some boards might be closed, specially if the system is stopping due to losing connection to one of the boards</remarks>
        private readonly BaseSensorBox _reader;
        private static readonly object ControllerInitializationLock = new object();
        private PluginsCommandHandler _cmd;
        private static SwitchBoardController _instance;
        private readonly TaskCompletionSource _boardControlLoopsStopped = new TaskCompletionSource();
        private readonly CancellationTokenSource _boardLoopsStopTokenSource = new CancellationTokenSource();

        private SwitchBoardController(CommandHandler cmd) 
        {
            _cmd = new PluginsCommandHandler(cmd);
            var heaters = IOconfFile.GetHeater().Cast<IOconfOut230Vac>();
            var ports = heaters.Concat(IOconfFile.GetValve()).Where(p => p.Map.Board != null).ToList();
            var boardsTemperatures = ports.GroupBy(p => p.BoxName).Select(b => b.Select(p => p.GetBoardTemperatureInputConf()).FirstOrDefault());
            var inputs = ports.SelectMany(p => p.GetExpandedInputConf()).Concat(boardsTemperatures);
            _reader = new BaseSensorBox(cmd, "switchboards", string.Empty, "show switchboards inputs", inputs);
            _reader.Stopping += WaitForLoopStopped;
            cmd.AddCommand("escape", Stop);
            Task.Run(() => RunBoardControlLoops(ports));
        }

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
            var lastActions = new SwitchboardAction[ports.Count];
            var boardStateName = board.BoxName + "_state";
            var waitingBoardReconnect = false;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var vector = await _cmd.When(_ => true, token);
                    if (!CheckConnectedStateInVector(board, boardStateName, ref waitingBoardReconnect, vector)) 
                        continue; // no point trying to send commands while there is no connection to the board.

                    foreach (var port in ports)
                        await DoPortActions(vector, board, port, lastActions, token);
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
                CALog.LogInfoAndConsoleLn(LogID.A, $"resuming switchboard actions after reconnect on {board}");
                waitingBoardReconnect = false;
            }
            else if (!waitingBoardReconnect && !connected)
            {
                CALog.LogInfoAndConsoleLn(LogID.A, $"stopping switchboard actions while connection is reestablished on {board}");
                waitingBoardReconnect = true;
            }
            return connected;
        }

        private async Task RunBoardControlLoops(List<IOconfOut230Vac> ports)
        {
            DateTime start = DateTime.Now;
            try
            {
                var boardLoops = ports
                    .GroupBy(v => v.Map.Board)
                    .Select(g => BoardLoop(g.Key, g.ToList(), _boardLoopsStopTokenSource.Token))
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
        
        private async Task DoPortActions(NewVectorReceivedArgs vector, MCUBoard board, IOconfOut230Vac port, SwitchboardAction[] lastActions, CancellationToken token)
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
            catch (Exception)
            {
                CALog.LogErrorAndConsoleLn(LogID.A, $"Failed executing port action for board {board.BoxName} port {port.Name} - {port.Name}");
                throw; // this will log extra info and avoid extra board actions on this cycle
            }
        }

        private void AllOff(MCUBoard board, List<IOconfOut230Vac> ports)
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
                CALog.LogErrorAndConsoleLn(LogID.A, $"Error detected while attempting to set ports to default positions for board {board.BoxName}", ex);
            }
        }

        private void WaitForLoopStopped(object sender, EventArgs args)
        {
            CALog.LogData(LogID.A, "waiting for switchboards control loops to finish actions and set ports to their default value");
            _boardControlLoopsStopped.Task.Wait();
            CALog.LogData(LogID.A, "finished waiting for switchboards loops");
        }
    }
}
