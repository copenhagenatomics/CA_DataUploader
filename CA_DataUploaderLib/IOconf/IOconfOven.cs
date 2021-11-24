using System;
using System.Collections.Generic;
using System.Linq;
using CA_DataUploaderLib.Extensions;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfOven : IOconfDriver
    {
        public IOconfOven(string row, int lineNum) : base(row, lineNum, "Oven")
        {
            format = "Oven;Area;HeatingElement;TypeK;[ProportionalGain];[ControlPeriod];[MaxOutputPercentage]";

            var list = ToList();
            if (!int.TryParse(list[1], out OvenArea)) 
                throw new Exception($"IOconfOven: wrong OvenArea number: {row} {format}");
            if (OvenArea < 1)
                throw new Exception("Oven area must be a number bigger or equal to 1");
            
            HeatingElement = IOconfFile.GetHeater().Single(x => x.Name == list[2]);
            TemperatureSensorName = list[3];
            var filter = IOconfFile.GetFilters().SingleOrDefault(x => x.NameInVector == TemperatureSensorName);
            if (filter != null)
            {
                TypeKs = IOconfFile.GetTypeK().Where(x => filter.SourceNames.Contains(x.Name)).ToList();
                if (TypeKs.Count == 0)
                    throw new Exception($"Failed to find source temperature sensors in filter {TemperatureSensorName} for oven");
            }
            else
            {
                TypeKs = IOconfFile.GetTypeK().Where(x => x.Name == TemperatureSensorName).ToList();
                if (TypeKs.Count == 0)
                    throw new Exception($"Failed to find temperature sensor {TemperatureSensorName} for oven");
            }
            BoardStateSensorNames = TypeKs.Select(k => k.BoardStateSensorName).ToList().AsReadOnly();

            if (list.Count < 5) return;
            if (!list[4].TryToDouble(out var proportionalGain))
                throw new Exception($"Failed to parse the specified proportional gain: {row}");
            ProportionalGain = proportionalGain;

            if (list.Count < 6) return;
            if (!TimeSpan.TryParse(list[5], out var controlPeriod))
                throw new Exception($"Failed to parse the specified control period: {row}");
            ControlPeriod = controlPeriod;

            if (list.Count < 7) return;
            if (!int.TryParse(list[6], out var maxOutputPercentage))
                throw new Exception($"Failed to parse the specified proportional gain: {row}");
            if (maxOutputPercentage < 0 || maxOutputPercentage > 100)
                throw new Exception($"Max output percentage must be a whole number between 0 and 100: {row}");
            MaxOutputPercentage = maxOutputPercentage / 100d;
        }

        public int OvenArea;
        public IOconfHeater HeatingElement;
        public bool IsTemperatureSensorInitialized => TypeKs.All(k => k.IsInitialized());
        public string TemperatureSensorName { get; }
        public IReadOnlyCollection<string> BoardStateSensorNames {get;}
        //with the current formula the gain pretty much means seconds to gain 1C
        //by default we assume the HeatingElement can heat TypeK 5 degrees x second on. 
        public double ProportionalGain { get; } = 0.2d; 
        public TimeSpan ControlPeriod { get; } = TimeSpan.FromSeconds(30);
        public double MaxOutputPercentage { get; } = 1d;
        private readonly List<IOconfTypeK> TypeKs;
    }
}
