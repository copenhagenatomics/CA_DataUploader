using System;
using System.Collections.Generic;
using System.Diagnostics;
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
            foreach (var board in ports.Where(p => p.Map.IsLocalBoard && p.Map.Board != null).GroupBy(v => v.Map.Board))
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
            var lastActions = Enumerable.Range(0, ports.Max(p => p.PortNumber))
                .Select(_ => (isOn: false, timeToRepeat: default(DateTime), timeRunnin: Stopwatch.StartNew()))
                .ToArray();
            var portsFields = ports.Select(p => (field: p.Name + "_On/Off", number: p.PortNumber)).ToArray();
            reader.AddBuildInWriteAction(board, WriteAction, ExitAction);

            Task ExitAction(MCUBoard board, CancellationToken token) => AllOff(board, ports, token);
            async Task WriteAction(NewVectorReceivedArgs vector, MCUBoard board, CancellationToken token)
            {
                foreach (var port in portsFields)
                    await DoPortActions(vector, board, port, lastActions, token);
            }
        }

        private static async Task DoPortActions(NewVectorReceivedArgs vector, MCUBoard board, (string field, int number) port, (bool isOn, DateTime timeToRepeat, Stopwatch timeRunning)[] lastActions, CancellationToken token)
        {
            if (vector == null)
            { //too long time without receiving a vector, lets ensure the port is off.
              //note: we are assuming we get called periodically at a reasonable frequency to reassert the off, but for now we are not guarding against too frequent executions
                await board.SafeWriteLine($"p{port.number} off", token);
                lastActions[port.number - 1].isOn = false;
                lastActions[port.number - 1].timeToRepeat = DateTime.MaxValue;//max forces execution on the next vector / also note we don't restart time running for the same reason.
            }

            var isOn = vector[port.field] == 1.0;
            var lastAction = lastActions[port.number - 1];
            var currentVectorTime = vector.GetVectorTime();
            if (lastAction.isOn == isOn && lastAction.timeRunning.ElapsedMilliseconds < 2000 && lastAction.timeToRepeat < currentVectorTime)
                // no action changes has been requested since the last action was executed and there has been less than 2 seconds
                // note the 2 seconds is checked in 2 diff ways to avoid it being missed due to bit flips.
                // Note that this does not guard on too frequent executions, but at least we are resetting both values on each execution.
                return; 
                
            if (!isOn)
                await board.SafeWriteLine($"p{port.number} off", token);
            else
                await board.SafeWriteLine($"p{port.number} on 3", token);
            lastActions[port.number - 1].isOn = isOn;
            lastActions[port.number - 1].timeToRepeat = currentVectorTime.AddMilliseconds(2000);
            lastActions[port.number - 1].timeRunning.Restart();
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
