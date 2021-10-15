using CA_DataUploaderLib.IOconf;
using CA_DataUploaderLib.Extensions;
using System.Collections.Generic;
using System.Linq;
using CA_DataUploaderLib.Helpers;
using System;
using System.Threading.Tasks;
using System.Threading;

namespace CA_DataUploaderLib
{
    public class ThermocoupleBox : BaseSensorBox
    {
        private readonly SensorSample _rpiGpuSample;
        private readonly SensorSample _rpiCpuSample;
        public ThermocoupleBox(CommandHandler cmd) : base(cmd, "Temperatures", string.Empty, "show all temperatures in input queue", GetSensors()) 
        { // these are disabled / null when we are running on windows, see GetSensors
            _rpiGpuSample = _values.FirstOrDefault(x => GetSensorName("Gpu").Equals(x.Name));
            _rpiCpuSample = _values.FirstOrDefault(x => GetSensorName("Cpu").Equals(x.Name));
        }

        protected override List<Task> StartReadLoops((IOconfMap map, SensorSample[] values)[] boards, CancellationToken token)
        {
            var loops = base.StartReadLoops(boards, token);
            if (_rpiGpuSample != null || _rpiCpuSample != null) 
                loops.Add(ReadRpiTemperaturesLoop(_rpiGpuSample, _rpiCpuSample, token));
            return loops;
        }

        private static async Task ReadRpiTemperaturesLoop(SensorSample gpuSample, SensorSample cpuSample, CancellationToken token)
        {
            var msBetweenReads = 1000; // waiting every second, for higher resolution.
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(msBetweenReads, token);
                    gpuSample.Value = DULutil.ExecuteShellCommand("vcgencmd measure_temp").Replace("temp=", "").Replace("'C", "").ToDouble();
                    cpuSample.Value = DULutil.ExecuteShellCommand("cat /sys/class/thermal/thermal_zone0/temp").ToDouble() / 1000;
                }
                catch (TaskCanceledException ex)
                {
                    if (!token.IsCancellationRequested)
                        CALog.LogErrorAndConsoleLn(LogID.A, ex.ToString());
                }
                catch (Exception ex)
                { 
                    CALog.LogErrorAndConsoleLn(LogID.A, $"Unexpected error reading rpi temperatures", ex);
                }
            }
            
        }

        private static IEnumerable<IOconfInput> GetSensors()
        {
            var values = IOconfFile.GetTypeKAndLeakage();
            var rpiTemp = IOconfFile.GetRPiTemp();
            var addRpiTemp = !rpiTemp.Disabled && !OperatingSystem.IsWindows();
            return addRpiTemp
                ? values.Concat(new[] { rpiTemp.WithName(GetSensorName("Gpu")), rpiTemp.WithName(GetSensorName("Cpu")) })
                : values;
        }

        private static string GetSensorName(string suffix) => IOconfFile.GetLoopName() + "-" + IOconfFile.GetRPiTemp().Name + suffix;
    }
}
