using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CA.LoopControlPluginBase;
using CA_DataUploaderLib.Extensions;
using CA_DataUploaderLib.IOconf;

namespace CA_DataUploaderLib
{
    public sealed class SwitchBoardController : IDisposable
    {
        private readonly BaseSensorBox _boardsLoops;
        private static readonly object ControllerInitializationLock = new();
        private static SwitchBoardController _instance;

        private SwitchBoardController(CommandHandler cmd) 
        {
            var ports = IOconfFile.GetEntries<IOconfOut230Vac>().Where(p => p.IsSwitchboardControllerOutput).ToList();
            var boardsTemperatures = ports.GroupBy(p => p.BoxName).Select(b => b.Select(p => p.GetBoardTemperatureInputConf()).FirstOrDefault());
            var sensorPortsInputs = IOconfFile.GetEntries<IOconfSwitchboardSensor>().SelectMany(i => i.GetExpandedConf());
            var inputs = ports.SelectMany(p => p.GetExpandedInputConf()).Concat(boardsTemperatures).Concat(sensorPortsInputs);
            _boardsLoops = new BaseSensorBox(cmd, "switchboards", string.Empty, "show switchboards inputs", inputs);
            //we ignore remote boards and boards missing during the start sequence (as we don't have auto reconnect logic yet for those). Note the BaseSensorBox already reports the missing local boards.
            foreach (var board in ports.Where(p => p.Map.IsLocalBoard && p.Map.McuBoard != null).GroupBy(v => v.Map.McuBoard))
                RegisterBoardWriteActions(_boardsLoops, board.Key, board.ToList());
        }

        public void Dispose() => _boardsLoops.Dispose();
        public static SwitchBoardController GetOrCreate(CommandHandler cmd)
        {
            if (_instance != null) return _instance;
            lock (ControllerInitializationLock)
            {
                if (_instance != null) return _instance;
                return _instance = new SwitchBoardController(cmd);
            }
        }

        private static void RegisterBoardWriteActions(BaseSensorBox reader, MCUBoard board, List<IOconfOut230Vac> ports)
        {
            var lastActions = new SwitchboardAction[ports.Max(p => p.PortNumber)];
            reader.AddBuildInWriteAction(board, WriteAction, ExitAction); 

            Task ExitAction(MCUBoard board, CancellationToken token) => AllOff(board, ports, token);
            async Task WriteAction(NewVectorReceivedArgs vector, MCUBoard board, CancellationToken token)
            {
                foreach (var port in ports)
                    await DoPortActions(vector, board, port, lastActions, token);
            }
        }

        private static async Task DoPortActions(NewVectorReceivedArgs vector, MCUBoard board, IOconfOut230Vac port, SwitchboardAction[] lastActions, CancellationToken token)
        {
            var action = SwitchboardAction.FromVectorSamples(vector, port.Name);
            if (action.Equals(lastActions[port.PortNumber - 1]))
                return; // no action changes has been requested since the last action was executed
                
            var onSeconds = action.GetRemainingOnSeconds(vector.GetVectorTime());
            if (onSeconds <= 0)
                await board.SafeWriteLine($"p{port.PortNumber} off", token);
            else if (onSeconds == int.MaxValue)
                await board.SafeWriteLine($"p{port.PortNumber} on", token);
            else
                await board.SafeWriteLine($"p{port.PortNumber} on {onSeconds}", token);
            lastActions[port.PortNumber - 1] = action;
        }

        private static async Task AllOff(MCUBoard board, List<IOconfOut230Vac> ports, CancellationToken token)
        {
            await board.SafeWriteLine("off", token); 
            foreach (var port in ports)
                if (port.HasOnSafeState)
                    await board.SafeWriteLine($"p{port.PortNumber} on", token);
        }
    }
}
