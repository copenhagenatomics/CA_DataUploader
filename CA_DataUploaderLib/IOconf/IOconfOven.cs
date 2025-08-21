#nullable enable
using System;
using System.Collections.ObjectModel;
using System.Linq;
using CA.LoopControlPluginBase;
using CA_DataUploaderLib.Extensions;
using static CA_DataUploaderLib.HeatingController;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfOven : IOconfDriver, IIOconfRowWithDecision
    {
        public IOconfOven(string row, int lineNum) : base(row, lineNum, "Oven")
        {
            Format = "Oven;Area;HeatingElement;TypeK/TypeJ;[ProportionalGain];[ControlPeriod];[MaxOutputPercentage]";

            var list = ToList();
            if (!int.TryParse(list[1], out OvenArea)) 
                throw new FormatException($"Oven area must be a number: {row} {Format}");
            if (OvenArea < 1)
                throw new FormatException("Oven area must be a number bigger than or equal to 1");

            HeaterName = list[2];
            TemperatureSensorName = list[3];

            if (list.Count < 5) return;
            if (!list[4].TryToDouble(out var proportionalGain))
                throw new FormatException($"Failed to parse the specified proportional gain: {row}");
            ProportionalGain = proportionalGain;

            if (list.Count < 6) return;
            if (!TimeSpan.TryParse(list[5], out var controlPeriod))
                throw new FormatException($"Failed to parse the specified control period: {row}");
            ControlPeriod = controlPeriod;

            if (list.Count < 7) return;
            if (!int.TryParse(list[6], out var maxOutputPercentage))
                throw new FormatException($"Failed to parse the specified proportional gain: {row}");
            if (maxOutputPercentage < 0 || maxOutputPercentage > 100)
                throw new FormatException($"Max output percentage must be a whole number between 0 and 100: {row}");
            MaxOutputPercentage = maxOutputPercentage / 100d;
        }

        public override void ValidateDependencies(IIOconf ioconf)
        {
            var list = ToList();
            HeatingElement = ioconf.GetHeater().SingleOrDefault(x => x.Name == HeaterName) ?? throw new FormatException($"Failed to find HeatingElement: {HeaterName} for oven: {Row}");
            var isTemp = ioconf.GetTemp().Any(t => t.Name == TemperatureSensorName);
            var isMath = ioconf.GetMath().Any(m => m.Name == TemperatureSensorName);
            var isFilter = ioconf.GetFilters().Any(f => f.NameInVector == TemperatureSensorName);
            var isRedundancy = ioconf.GetEntries<Redundancy.IOconfRedundant>().Any(f => f.Name == TemperatureSensorName);
            if (!isTemp && !isMath && !isFilter && !isRedundancy)
                throw new FormatException($"Failed to find sensor: {TemperatureSensorName} for oven: {Row}");
        }

        public ReadOnlyCollection<string> GetBoardStateNames(IIOconf ioconf) => ioconf.GetBoardStateNames(TemperatureSensorName).ToList().AsReadOnly();

        public LoopControlDecision CreateDecision(IIOconf ioconf) => new OvenAreaDecision(new($"ovenarea{OvenArea}", OvenArea, ioconf.GetEntries<IOconfOvenProportionalControlUpdates>().SingleOrDefault()));

        public override string UniqueKey()
        {
            var list = ToList();
            return list[0] + list[2] + list[3];  // you could argue that this should somehow include 1 too. 
        }

        private IOconfHeater? heatingElement;

        public readonly int OvenArea;

        public IOconfHeater HeatingElement
        { 
            get => heatingElement ?? throw new MemberAccessException($"Call {nameof(ValidateDependencies)} before accessing {nameof(HeatingElement)}."); 
            private set => heatingElement = value; 
        }
        public string TemperatureSensorName { get; }
        public string HeaterName { get; }

        //with the current formula the gain pretty much means seconds to gain 1C
        //by default we assume the HeatingElement can heat the temperature sensor 5 degrees x second on. 
        public double ProportionalGain { get; } = 0.2d; 
        public TimeSpan ControlPeriod { get; } = TimeSpan.FromSeconds(30);
        public double MaxOutputPercentage { get; } = 1d;
    }
}
