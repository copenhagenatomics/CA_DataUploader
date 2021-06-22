using CA_DataUploaderLib.Extensions;
using System;
using System.Linq;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfSafeValve : IOconfRow
    {
        public string Name { get; }
        public string ValveName { get; }
        /// <summary>sensor name as appears in the vector</summary>
        /// <remarks>only pressures, filters and math are allowed</remarks>
        public string SensorName { get; }
        /// <summary>max sensor value. Above this value the safe valve triggers, setting the valve to the default/safe position</summary>
        public double Max { get; }

        public IOconfSafeValve(string row, int lineNum) : base(row, lineNum, "SafeValve")
        {
            format = "SafeValve;Name;ValveName;SensorName;Max";

            var list = ToList();
            if (list.Count < 5)
                throw new Exception($"Wrong SafeValve type: {row} {Environment.NewLine}{format}");
            Name = list[1];
            ValveName = list[2];
            SensorName = list[3];
            if (!list[4].TryToDouble(out var max))
                throw new Exception($"Wrong filter length: {row} {Environment.NewLine}{format}");
            Max = max;

            if  (!IOconfFile.GetValve().Any(v => v.Name == ValveName))
                throw new Exception($"Failed to find valve: {ValveName} for safevalve: {row}");
            var foundSensor = 
                IOconfFile.GetPressure().Any(p => p.Name == SensorName) ||
                IOconfFile.GetMath().Any(m => m.Name == SensorName) ||
                IOconfFile.GetFilters().Any(m => m.NameInVector == SensorName);
            if (!foundSensor)
                throw new Exception($"Failed to find sensor: {SensorName} for safevalve: {row}");
        }
    }
}
