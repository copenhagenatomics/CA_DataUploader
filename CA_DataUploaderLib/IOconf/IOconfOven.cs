#nullable enable
using System;
using System.Collections.ObjectModel;
using System.Linq;
using CA_DataUploaderLib.Extensions;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfOven : IOconfDriver
    {
        public IOconfOven(string row, int lineNum) : base(row, lineNum, "Oven")
        {
            Format = "Oven;Area;HeatingElement;TypeK/TypeJ;[ProportionalGain];[ControlPeriod];[MaxOutputPercentage]";

            var list = ToList();
            if (!int.TryParse(list[1], out OvenArea)) 
                throw new Exception($"IOconfOven: wrong OvenArea number: {row} {Format}");
            if (OvenArea < 1)
                throw new Exception("Oven area must be a number bigger or equal to 1");

            HeaterName = list[2];
            TemperatureSensorName = list[3];

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

        public override void ValidateDependencies()
        {
            var list = ToList();
            HeatingElement = IOconfFile.GetHeater().SingleOrDefault(x => x.Name == HeaterName) ?? throw new FormatException($"Failed to find HeatingElement: {HeaterName} for oven: {Row}");
            var isTemp = IOconfFile.GetTemp().Any(t => t.Name == TemperatureSensorName);
            var isMath = IOconfFile.GetMath().Any(m => m.Name == TemperatureSensorName);
            var isFilter = IOconfFile.GetFilters().Any(f => f.NameInVector == TemperatureSensorName);
            var isRedundancy = IOconfFile.GetEntries<Redundancy.IOconfRedundant>().Any(f => f.Name == TemperatureSensorName);
            if (!isTemp && !isMath && !isFilter && !isRedundancy)
                throw new FormatException($"Failed to find sensor: {TemperatureSensorName} for oven: {Row}");
            BoardStateSensorNames = IOconfFile.GetBoardStateNames(TemperatureSensorName).ToList().AsReadOnly();
        }

        private IOconfHeater? heatingElement;
        private ReadOnlyCollection<string>? boardStateSensorNames;

        public readonly int OvenArea;

        public IOconfHeater HeatingElement
        { 
            get => heatingElement ?? throw new Exception($"Call {nameof(ValidateDependencies)} before accessing {nameof(HeatingElement)}."); 
            private set => heatingElement = value; 
        }
        public string TemperatureSensorName { get; }
        public string HeaterName { get; }
        public ReadOnlyCollection<string> BoardStateSensorNames 
        { 
            get => boardStateSensorNames ?? throw new Exception($"Call {nameof(ValidateDependencies)} before accessing {nameof(BoardStateSensorNames)}.");
            private set => boardStateSensorNames = value; 
        }
        //with the current formula the gain pretty much means seconds to gain 1C
        //by default we assume the HeatingElement can heat the temperature sensor 5 degrees x second on. 
        public double ProportionalGain { get; } = 0.2d; 
        public TimeSpan ControlPeriod { get; } = TimeSpan.FromSeconds(30);
        public double MaxOutputPercentage { get; } = 1d;
    }
}
