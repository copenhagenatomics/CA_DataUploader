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
        private readonly SensorSample? _rpiGpuSample;
        private readonly SensorSample? _rpiCpuSample;
        public ThermocoupleBox(CommandHandler cmd) : base(cmd, "Temperatures", GetSensors()) 
        { // these are disabled / null when RpiTemp is disabled
            _rpiGpuSample = _values.FirstOrDefault(x => IOconfRPiTemp.IsLocalGpuSensor(x.Input));
            _rpiCpuSample = _values.FirstOrDefault(x => IOconfRPiTemp.IsLocalCpuSensor(x.Input));
        }

        protected override List<Task> StartLoops((IOconfMap map, SensorSample.InputBased[] values, int boardStateIndexInFullVector)[] boards, CancellationToken token)
        {
            var loops = base.StartLoops(boards, token);
            if (_rpiGpuSample != null && _rpiCpuSample != null && !OperatingSystem.IsWindows()) 
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
            var values = IOconfFile.GetTypeK();
            var rpiTemp = IOconfFile.GetRPiTemp();
            var expandedSensors = rpiTemp.GetDistributedExpandedInputConf();
            return values.Concat(expandedSensors);
        }
    }
}
