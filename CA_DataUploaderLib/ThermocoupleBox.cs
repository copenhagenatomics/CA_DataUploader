using CA_DataUploaderLib.IOconf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CA_DataUploaderLib
{
    public class ThermocoupleBox : BaseSensorBox
    {
        public ThermocoupleBox(CommandHandler cmd = null, int filterLength = 1)
        {
            Title = "Thermocouples";
            Initialized = false;
            FilterLength = filterLength;
            _cmdHandler = cmd;

            _config = IOconfFile.GetTypeKAndLeakage().ToList();

            if (!_config.Any())
                return;

            if (cmd != null)
            {
                cmd.AddCommand("Temperatures", ShowQueue);
                cmd.AddCommand("help", HelpMenu);
                cmd.AddCommand("escape", Stop);
            }

            _boards = _config.Select(x => x.Map.Board).ToList();

            new Thread(() => this.LoopForever()).Start();
        }

        private bool HelpMenu(List<string> args)
        {
            CALog.LogInfoAndConsoleLn(LogID.A, $"temperatures              - show all temperatures in input queue");
            return true;
        }

    }
}
