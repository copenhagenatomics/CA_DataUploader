using CA_DataUploaderLib.IOconf;
using CA_DataUploaderLib.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using CA_DataUploaderLib.Helpers;

namespace CA_DataUploaderLib
{
    public class ThermocoupleBox : BaseSensorBox
    {
        private readonly SensorSample _rpiTempSample;
        public ThermocoupleBox(CommandHandler cmd)
        {
            Title = "Thermocouples";
            _cmd = cmd;
            _logLevel = IOconfFile.GetOutputLevel();

            _values = IOconfFile.GetTypeKAndLeakage().IsInitialized().Select(x => new SensorSample(x)).ToList();
            var rpiTemp = IOconfFile.GetRPiTemp();
            if (!rpiTemp.Disabled && !RpiVersion.IsWindows())
                _values.Add(_rpiTempSample = new SensorSample(rpiTemp));

            if (!_values.Any())
                return;

            if (cmd != null)
            {
                cmd.AddCommand("temperatures", ShowQueue);
                cmd.AddCommand("help", HelpMenu);
                cmd.AddCommand("Junction", Junction);
                cmd.AddCommand("escape", Stop);
            }

            _boards = _values.Where(x => !x.Input.Skip).Select(x => x.Input.Map.Board).Distinct().ToList();
            CALog.LogInfoAndConsoleLn(LogID.A, $"ThermocoupleBox boards: {_boards.Count()} values: {_values.Count()}");

            foreach (var board in _boards)
            {
                if(_values.Any(x=> x.Input.PortNumber == 0 && x.Input.BoxName == board.BoxName && ((IOconfTypeK)x.Input).AllJunction))
                    board.WriteLine("Junction");
            }

            new Thread(() => this.LoopForever()).Start();
        }

        protected override void ReadSensors()
        {
            base.ReadSensors();

            if (_rpiTempSample != null)
                _rpiTempSample.Value = DULutil.ExecuteShellCommand("vcgencmd measure_temp").Replace("temp=", "").Replace("'C", "").ToDouble();
        }

        private bool HelpMenu(List<string> args)
        {
            CALog.LogInfoAndConsoleLn(LogID.A, $"temperatures              - show all temperatures in input queue");
            return true;
        }

        private bool Junction(List<string> args)
        {
            foreach (var board in _boards)
                board.WriteLine("Junction");

            return true;
        }
    }
}
