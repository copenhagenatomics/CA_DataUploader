using CA_DataUploaderLib.IOconf;
using CA_DataUploaderLib.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace CA_DataUploaderLib
{
    public class ThermocoupleBox : BaseSensorBox
    {
        public ThermocoupleBox(CommandHandler cmd, TimeSpan filterLength)
        {
            Title = "Thermocouples";
            _cmdHandler = cmd;

            _config = IOconfFile.GetTypeKAndLeakage().IsInitialized().ToList();

            if (!_config.Any())
                return;

            if (cmd != null)
            {
                cmd.AddCommand("Temperatures", ShowQueue);
                cmd.AddCommand("help", HelpMenu);
                cmd.AddCommand("escape", Stop);
            }

            _boards = _config.Where(x => !x.Skip).Select(x => x.Map.Board).Distinct().ToList();
            _config.ForEach(x => _values.Add(x, new SensorSample(x, filterLength, GetHubID(x))));  // add in same order as in IO.conf
            CALog.LogInfoAndConsoleLn(LogID.A, $"ThermocoupleBox boards: {_boards.Count()} values: {_values.Count()}");

            new Thread(() => this.LoopForever()).Start();
        }

        private bool HelpMenu(List<string> args)
        {
            CALog.LogInfoAndConsoleLn(LogID.A, $"temperatures              - show all temperatures in input queue");
            return true;
        }

    }
}
