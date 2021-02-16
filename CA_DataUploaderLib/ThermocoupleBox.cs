using CA_DataUploaderLib.IOconf;
using CA_DataUploaderLib.Extensions;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using CA_DataUploaderLib.Helpers;

namespace CA_DataUploaderLib
{
    public class ThermocoupleBox : BaseSensorBox
    {
        private readonly SensorSample _rpiGpuSample;
        private readonly SensorSample _rpiCpuSample;
        private readonly TimeFrequencyThrottle _rpiTempsThrottle = new TimeFrequencyThrottle(5000); 
        public ThermocoupleBox(CommandHandler cmd)
        {
            Title = "Thermocouples";
            _cmd = cmd;
            _logLevel = IOconfFile.GetOutputLevel();

            _values = IOconfFile.GetTypeKAndLeakage().IsInitialized().Select(x => new SensorSample(x)).ToList();
            var rpiTemp = IOconfFile.GetRPiTemp();
            if (!rpiTemp.Disabled && !RpiVersion.IsWindows())
            {
                _values.Add(_rpiGpuSample = new SensorSample(rpiTemp.WithName(rpiTemp.Name + "Gpu")));
                _values.Add(_rpiCpuSample = new SensorSample(rpiTemp.WithName(rpiTemp.Name + "Cpu")));
            }

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
            if (_rpiGpuSample == null || _rpiCpuSample == null)
                return;

            bool shouldRun = _rpiTempsThrottle.ShouldRun(); // throttling of reads is an attempt to solve unexpected slow downs we have seen on some systems after enabling rpi temps by default
            _rpiGpuSample.Value = shouldRun ? DULutil.ExecuteShellCommand("vcgencmd measure_temp").Replace("temp=", "").Replace("'C", "").ToDouble() : _rpiGpuSample.Value;
            _rpiCpuSample.Value = shouldRun ? DULutil.ExecuteShellCommand("cat /sys/class/thermal/thermal_zone0/temp").ToDouble() / 1000 : _rpiGpuSample.Value;
            _rpiTempsThrottle.FinishedLastRun();
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
