using System.Collections.Generic;
using System.Linq;
using System.Threading;
using CA_DataUploaderLib.Extensions;
using CA_DataUploaderLib.IOconf;

namespace CA_DataUploaderLib
{
    public class GenericSensorBox : BaseSensorBox
    {
        public GenericSensorBox(CommandHandler cmd)
        {
            Title = "Generic Sensor Box";
            _cmd = cmd;
            _values = IOconfFile.GetGeneric().IsInitialized().Select(x => new SensorSample(x)).ToList();
            if (!_values.Any())
                return;

            if (cmd != null)
            {
                cmd.AddCommand("generic", ShowQueue);
                cmd.AddCommand("help", HelpMenu);
                cmd.AddCommand("escape", Stop);
            }

            _boards = _values.Where(x => !x.Input.Skip).Select(x => x.Input.Map.Board).Distinct().ToList();
            CALog.LogInfoAndConsoleLn(LogID.A, $"Generic Sensor boards: {_boards.Count()} values: {_values.Count()}");

            new Thread(() => this.LoopForever()).Start();
        }

        private bool HelpMenu(List<string> args)
        {
            CALog.LogInfoAndConsoleLn(LogID.A, $"generic              - show values for generic sensors");
            return true;
        }
    }
}