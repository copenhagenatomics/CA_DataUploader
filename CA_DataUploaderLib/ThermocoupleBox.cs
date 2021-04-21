using CA_DataUploaderLib.IOconf;
using CA_DataUploaderLib.Extensions;
using System.Collections.Generic;
using System.Linq;
using CA_DataUploaderLib.Helpers;
using System;

namespace CA_DataUploaderLib
{
    public class ThermocoupleBox : BaseSensorBox
    {
        private readonly SensorSample _rpiGpuSample;
        private readonly SensorSample _rpiCpuSample;
        public ThermocoupleBox(CommandHandler cmd) : base(cmd, "Temperatures", string.Empty, "show all temperatures in input queue", GetSensors()) 
        { // these are null when its not windows, see GetSensors
            _rpiGpuSample = _values.FirstOrDefault(x => x.Name.EndsWith("Gpu"));
            _rpiCpuSample = _values.FirstOrDefault(x => x.Name.EndsWith("Cpu"));
        }

        protected override void ReadSensors()
        {
            base.ReadSensors();

            if (_rpiGpuSample != null)
                _rpiGpuSample.Value = DULutil.ExecuteShellCommand("vcgencmd measure_temp").Replace("temp=", "").Replace("'C", "").ToDouble();
            if (_rpiCpuSample != null)
                _rpiCpuSample.Value = DULutil.ExecuteShellCommand("cat /sys/class/thermal/thermal_zone0/temp").ToDouble() / 1000;
        }

        private static IEnumerable<IOconfInput> GetSensors()
        {
            var values = IOconfFile.GetTypeKAndLeakage();
            var rpiTemp = IOconfFile.GetRPiTemp();
            var addRpiTemp = !rpiTemp.Disabled && !OperatingSystem.IsWindows();
            return addRpiTemp ? values.Concat(new []{ rpiTemp.WithName(rpiTemp.Name + "Gpu"), rpiTemp.WithName(rpiTemp.Name + "Cpu")}) : values;
        }
    }
}
