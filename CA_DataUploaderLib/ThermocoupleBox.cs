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

            _config = IOconfFile.GetTypeK().Cast<IOconfInput>().ToList();

            if (!_config.Any())
                throw new Exception("No TypeK temperature sensors found!");

            _cmdHandler = cmd;
            if (cmd != null)
            {
                cmd.AddCommand("Temperatures", ShowQueue);
                cmd.AddCommand("help", HelpMenu);
            }

            if (_config.Any())
                new Thread(() => this.LoopForever()).Start();
            else
                CALog.LogErrorAndConsole(LogID.A, "Type K thermocouple config information is missing in IO.conf");
        }

        private bool HelpMenu(List<string> args)
        {
            CALog.LogInfoAndConsoleLn(LogID.A, $"temperatures              - show all temperatures in input queue");
            return true;
        }

    }
}
