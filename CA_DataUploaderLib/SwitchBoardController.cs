#nullable enable
using System.Collections.Generic;
using System.Linq;
using CA_DataUploaderLib.IOconf;

namespace CA_DataUploaderLib
{
    public sealed class SwitchBoardController : BaseSensorBox
    {
        private static readonly object ControllerInitializationLock = new();
        private static SwitchBoardController? _instance;
        private static readonly List<IOconfOut230Vac> ports = IOconfFile.GetEntries<IOconfOut230Vac>().ToList();

        private SwitchBoardController(CommandHandler cmd) : base(cmd, "switchboards",
            ports.SelectMany(p => p.GetExpandedInputConf())
            .Concat(ports.GroupBy(p => p.BoxName).Select(g => g.First().GetBoardTemperatureInputConf()))
            .Concat(IOconfFile.GetEntries<IOconfSwitchboardSensor>().SelectMany(i => i.GetExpandedConf())))
        {
            //we ignore remote boards and boards missing during the start sequence (as we don't have auto reconnect logic yet for those). Note the BaseSensorBox already reports the missing local boards.
            foreach (var port in ports.Where(p => p.Map.IsLocalBoard && p.Map.Board != null))
                RegisterBoardWriteActions(port.Map.Board!, port, 0, port.Name + "_onoff", GetCommand);

            static string GetCommand(int portNumber, double target) => target == 1.0 ? $"p{portNumber} on 3" : $"p{portNumber} off";
        }

        public static void Initialize(CommandHandler cmd)
        {
            if (_instance != null) return;
            lock (ControllerInitializationLock)
                _instance ??= new SwitchBoardController(cmd);
        }
    }
}
