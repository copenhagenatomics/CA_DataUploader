using CA_DataUploaderLib.Extensions;
using System;
using System.Collections.Generic;
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
        public IReadOnlyCollection<string> BoardStateNames { get; }

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
            var pressures = IOconfFile.GetPressure().Where(p => p.Name == SensorName).ToList();
            var maths = IOconfFile.GetMath().Where(m => m.Name == SensorName).ToList();
            var filters = IOconfFile.GetFilters().Where(f => f.NameInVector == SensorName).ToList();
            var foundSensor = 
                pressures.Count > 0 ||
                maths.Count > 0 ||
                filters.Count > 0;
            if (!foundSensor)
                throw new Exception($"Failed to find sensor: {SensorName} for safevalve: {row}");
            // note this does not currently include math sources. It is technically possible but we need to get the sources referenced in the math expression.
            BoardStateNames =
                pressures.Select(p => p.BoardStateSensorName)
                .Concat(GetBoardStateNamesForSensors(maths.SelectMany(p => p.GetSources())))
                .Concat(GetBoardStateNamesForSensors(filters.SelectMany(f => f.SourceNames)))
                .ToList()
                .AsReadOnly();
        }

        private IEnumerable<string> GetBoardStateNamesForSensors(IEnumerable<string> sensors)
        {
            var targetSources = sensors.ToHashSet();
            foreach (var input in IOconfFile.GetInputs()) // since we call it in the ctor, it includes only inputs before the safe valve
            {
                if (targetSources.Contains(input.Name))
                    yield return input.BoardStateSensorName;
            }
        }
    }
}
