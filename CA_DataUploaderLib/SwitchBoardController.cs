#nullable enable
using System.Collections.Generic;
using System.Linq;
using CA_DataUploaderLib.IOconf;

namespace CA_DataUploaderLib
{
    public sealed class SwitchBoardController : BaseSensorBox
    {
        private static readonly object ControllerInitializationLock = new();
        private static readonly Dictionary<CommandHandler, SwitchBoardController> _instanceDictionary = [];

        private SwitchBoardController(IIOconf ioconf, CommandHandler cmd) : base(cmd, "switchboards",
            ioconf.GetEntries<IOconfOut230Vac>().SelectMany(p => p.GetExpandedInputConf())
            .Concat(ioconf.GetEntries<IOconfOut230Vac>().GroupBy(p => p.BoxName).Select(g => g.First().GetBoardTemperatureInputConf()))
            .Concat(ioconf.GetEntries<IOconfSwitchboardSensor>().SelectMany(i => i.GetExpandedConf())))
        {
            //we ignore remote boards and boards missing during the start sequence (as we don't have auto reconnect logic yet for those). Note the BaseSensorBox already reports the missing local boards.
            foreach (var port in ioconf.GetEntries<IOconfOut230Vac>().Where(p => p.Map.IsLocalBoard && p.Map.McuBoard != null))
                RegisterBoardWriteActions(port.Map.McuBoard!, port, 0, port.Name + "_onoff", GetCommand);

            static string GetCommand(int portNumber, double target) => target > 0.0 ? $"p{portNumber} on 3 {target:0%}" : $"p{portNumber} off";
        }

        public static void Initialize(IIOconf ioconf, CommandHandler cmd)
        {
            lock (ControllerInitializationLock)
            {
                if (_instanceDictionary.ContainsKey(cmd)) return;
                _instanceDictionary[cmd] = new SwitchBoardController(ioconf, cmd);
            }
            cmd.StopToken.Register(() => { lock (ControllerInitializationLock) _instanceDictionary.Remove(cmd); });
        }
    }
}
