using CA.LoopControlPluginBase;
using CA_DataUploaderLib.Extensions;
using CA_DataUploaderLib.IOconf;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CA_DataUploaderLib
{
    public class Redundancy
    {
        public Redundancy(IIOconf ioconf, CommandHandler cmd)
        {
            var configs = IOconfRedundant.ToDecisionConfigs(ioconf.GetEntries<IOconfRedundant>(), ioconf);
            cmd.AddDecisions(configs.Select(r => new Decision(r)).ToList());
            SwitchBoardController.Initialize(ioconf, cmd);
        }

        public static void RegisterSystemExtensions(IIOconfLoader loader)
        {
            loader.AddLoader(IOconfRedundantSensors.RowType, (row, lineNumber) => new IOconfRedundantSensors(row, lineNumber));
            loader.AddLoader(IOconfRedundantValidRange.RowType, (row, lineNumber) => new IOconfRedundantValidRange(row, lineNumber));
            loader.AddLoader(IOconfRedundantInvalidDefault.RowType, (row, lineNumber) => new IOconfRedundantInvalidDefault(row, lineNumber));
            loader.AddLoader(IOconfRedundantStrategy.RowType, (row, lineNumber) => new IOconfRedundantStrategy(row, lineNumber));
            loader.AddLoader(IOconfRedundantInvalidValueDelay.RowType, (row, lineNumber) => new IOconfRedundantInvalidValueDelay(row, lineNumber));
        }

        public enum RedundancyStrategy { Median, Max, Min, Average }
        public class IOconfRedundant : IOconfRow
        {
            protected IOconfRedundant(string row, int lineNum, string type) : base(row, lineNum, type) { }

            public override IEnumerable<string> GetExpandedSensorNames() => [Name];

            public static IEnumerable<Decision.Config> ToDecisionConfigs(IEnumerable<IOconfRedundant> redundants, IIOconf ioconf)
            {
                var invalidValueDelay = redundants.OfType<IOconfRedundantInvalidValueDelay>().SingleOrDefault()?.InvalidValueDelay ?? 0;
                var grouped = redundants.Where(r => r is not IOconfRedundantInvalidValueDelay).GroupBy(r => r.Name).ToList();
                foreach (var group in grouped)
                {
                    var configs = group.ToList();
                    var sensorsConfig = configs.OfType<IOconfRedundantSensors>().SingleOrDefault()
                        ?? throw new FormatException($"Redundancy - missing sensors for: {Environment.NewLine + string.Join(Environment.NewLine, configs.Select(c => c.Row))}");
                    var validRange = configs.OfType<IOconfRedundantValidRange>().SingleOrDefault()?.ValidRange ?? (double.MinValue, double.MaxValue);
                    var invalidDefault = configs.OfType<IOconfRedundantInvalidDefault>().SingleOrDefault()?.InvalidDefault ?? 10000;
                    var strategy = configs.OfType<IOconfRedundantStrategy>().SingleOrDefault()?.Strategy ?? RedundancyStrategy.Median;
                    yield return new(sensorsConfig.Name, sensorsConfig.Sensors, sensorsConfig.GetBoardStateNames(ioconf), validRange, invalidDefault, strategy, invalidValueDelay);
                }
            }
        }

        private class IOconfRedundantSensors : IOconfRedundant
        {
            public const string RowType = "RedundantSensors";

            public List<string> Sensors { get; private set; }

            public IOconfRedundantSensors(string row, int lineNum) : base(row, lineNum, RowType)
            {
                ExpandsTags = true;
                Sensors = ToList().Skip(2).ToList();
                if (Sensors.Count < 1)
                    throw new FormatException($"Missing sensors. Format: {RowType};Name;Sensor1;Sensor2...;Sensorn. Line: {Row}");
            }

            protected internal override void UseTags(ILookup<string, IOconfRow> rowsByTag)
            {
                base.UseTags(rowsByTag);
                Sensors = ToList().Skip(2).ToList(); //Update the sensors list with any expanded tags
            }

            public override void ValidateDependencies(IIOconf ioconf)
            {
                foreach (var sensor in Sensors)
                    if (!ioconf.GetEntries<IOconfRow>().Any(e => e.GetExpandedSensorNames().Contains(sensor)))
                        throw new FormatException($"Failed to find {sensor} for {RowType}: {Row}");
            }

            public List<List<string>> GetBoardStateNames(IIOconf ioconf)
            {
                var boardStateNames = new List<List<string>>();
                for (int i = 0; i < Sensors.Count; i++)
                    boardStateNames.Add(new List<string>(ioconf.GetBoardStateNames(Sensors[i])));
                return boardStateNames;
            }
        }

        private class IOconfRedundantValidRange : IOconfRedundant
        {
            public const string RowType = "RedundantValidRange";

            public (double min, double max) ValidRange { get; }

            public IOconfRedundantValidRange(string row, int lineNum) : base(row, lineNum, RowType)
            {
                var vals = ToList();
                if (vals.Count < 4) throw new FormatException($"Too few values. Format: {RowType};Name;MinValue;MaxValue. Row {Row}");
                if (!vals[2].TryToDouble(out var min) || !vals[3].TryToDouble(out var max))
                    throw new FormatException($"Failed to parse min/max. Format: {RowType};Name;MinValue;MaxValue. Row {Row}");

                ValidRange = (min, max);
            }
        }

        private class IOconfRedundantInvalidDefault : IOconfRedundant
        {
            public const string RowType = "RedundantInvalidDefault";

            public double InvalidDefault { get; }

            public IOconfRedundantInvalidDefault(string row, int lineNum) : base(row, lineNum, RowType)
            {
                var vals = ToList();
                if (vals.Count < 3) throw new FormatException($"Too few values. Format: {RowType};Name;InvalidDefaultValue. Row {Row}");
                if (!vals[2].TryToDouble(out var invalidDefault))
                    throw new FormatException($"Failed to parse invalid default value. Format: {RowType};Name;InvalidDefaultValue. Row {Row}");

                InvalidDefault = invalidDefault;
            }
        }

        private class IOconfRedundantStrategy : IOconfRedundant
        {
            public const string RowType = "RedundantStrategy";

            public IOconfRedundantStrategy(string row, int lineNum) : base(row, lineNum, RowType)
            {
                var vals = ToList();
                if (vals.Count < 3) throw new FormatException($"Too few values. Format: {RowType};Name;Median/Max/Min/Average. Row {Row}");
                if (!Enum.TryParse<RedundancyStrategy>(vals[2], out var redundancyStrategy))
                    throw new FormatException($"Failed to parse strategy. Format: {RowType};Name;Strategy. Row {Row}");

                Strategy = redundancyStrategy;
            }

            public RedundancyStrategy Strategy { get; }
        }

        private class IOconfRedundantInvalidValueDelay : IOconfRedundant
        {
            public const string RowType = "RedundantInvalidValueDelay";

            /// <summary>
            /// In seconds.
            /// </summary>
			public double InvalidValueDelay { get; }

            public IOconfRedundantInvalidValueDelay(string row, int lineNum) : base(row, lineNum, RowType)
            {
                var vals = ToList();
                if (vals.Count < 2) throw new FormatException($"Too few values. Format: {RowType};InvalidValueDelay. Row {Row}");
                if (!vals[1].TryToDouble(out var invalidValueDelay))
                    throw new FormatException($"Failed to parse invalid value delay. Format: {RowType};InvalidValueDelay. Row {Row}");

                InvalidValueDelay = invalidValueDelay;
            }

            public override string UniqueKey() => Type;
            protected override void ValidateName(string name) { } // no validation
        }


        public class Decision : LoopControlDecision
        {
            public enum Events { none, vector };
            private readonly Config _config;

            private Indexes? _indexes;
            public override string Name => _config.Name;
            public override PluginField[] PluginFields => _config.InvalidValueDelay > 0 ? [Name, $"{Name}_invalidValueDelay"] : [Name];
            public override string[] HandledEvents { get; } = [];
            public Decision(Config config) => _config = config;
            public override void Initialize(CA.LoopControlPluginBase.VectorDescription desc) => _indexes = new(desc, _config);
            public override void MakeDecision(CA.LoopControlPluginBase.DataVector vector, List<string> events)
            {
                if (_indexes == null) throw new InvalidOperationException("Unexpected call to MakeDecision before Initialize was called first");
                var model = new Model(vector, _indexes, _config);
                model.MakeDecision();
            }

            public class Config
            {
                public Config(string name, List<string> sensors, List<List<string>> sensorBoardStates, (double min, double max) validRange, double defaultInvalidValue, RedundancyStrategy strategy, double invalidValueDelay = 0)
                {
                    Name = name;
                    Sensors = sensors;
                    SensorBoardStates = sensorBoardStates;
                    if (sensors.Count != sensorBoardStates.Count)
                        throw new ArgumentException($"Redundancy: {name} - sensors.Count must be equal to sensorBoardStates.Count");
                    ValidRange = validRange;
                    DefaultInvalidValue = defaultInvalidValue;
                    InvalidValueDelay = invalidValueDelay;
                    Strategy = strategy;
                    ReusableBuffer = new double[sensors.Count];
                }

                public List<string> Sensors { get; }
                public (double min, double max) ValidRange { get; }
                public double[] ReusableBuffer { get; }
                public double DefaultInvalidValue { get; }
                public double InvalidValueDelay { get; }
                public RedundancyStrategy Strategy { get; }
                public List<List<string>> SensorBoardStates { get; }
                public string Name { get; }

                public double Calculate(Span<double> values) => Strategy switch
                {
                    RedundancyStrategy.Median => Median(values),
                    RedundancyStrategy.Max => Max(values),
                    RedundancyStrategy.Min => Min(values),
                    RedundancyStrategy.Average => Average(values),
                    _ => throw new ArgumentException($"Unexpected redundancy strategy: {Strategy}")
                };

                /// <remarks>Important: this method has the side effect of sorting the received <see cref="Span{double}"/></remarks>
                static double Median(Span<double> values)
                {
                    values.Sort();
                    int middle = values.Length / 2;
                    return values.Length % 2 != 0 ? values[middle] : (values[middle - 1] + values[middle]) / 2;
                }

                private static double Average(Span<double> values)
                {
                    double sum = 0;
                    foreach (var v in values)
                        sum += v;
                    return sum / values.Length;
                }

                private static double Min(Span<double> values)
                {
                    double min = double.MaxValue;
                    foreach (var v in values)
                        min = Math.Min(min, v);

                    return min;
                }

                private static double Max(Span<double> values)
                {
                    double max = double.MinValue;
                    foreach (var v in values)
                        max = Math.Max(max, v);
                    return max;
                }
            }

#pragma warning disable IDE1006 // Naming Styles - decisions are coded using a similar approach to decisions plugins, which avoid casing rules in properties to more have naming more similar to the original fields
            public readonly ref struct Model
            {
                private readonly Indexes _indexes;
                private readonly Config _config;
                private readonly CA.LoopControlPluginBase.DataVector _latestVector;

                public Model(CA.LoopControlPluginBase.DataVector latestVector, Indexes indexes, Config config)
                {
                    _latestVector = latestVector;
                    _indexes = indexes;
                    _config = config;
                }
                public double value { get => _latestVector[_indexes.value]; set => _latestVector[_indexes.value] = value; }
                public double invalidValueDelay { get => _latestVector[_indexes.invalidValueDelay]; set => _latestVector[_indexes.invalidValueDelay] = value; }

                internal void MakeDecision()
                {
                    var validValues = _config.ReusableBuffer.AsSpan();
                    var validValuesCount = 0;

                    for (int i = 0; i < _indexes.sensors.Length; i++)
                    {
                        if (!BoardsConnected(i)) continue;

                        var val = _latestVector[_indexes.sensors[i]];
                        if (val < _config.ValidRange.min || val > _config.ValidRange.max) continue;

                        validValues[validValuesCount++] = val;
                    }

                    validValues = validValues[..validValuesCount];
                    if (!validValues.IsEmpty)
                    {
                        value = _config.Calculate(validValues);
                        if (_config.InvalidValueDelay > 0)
                            invalidValueDelay = 0.0;
                    }
                    else if (_config.InvalidValueDelay == 0)
                        value = _config.DefaultInvalidValue;
                    else if (invalidValueDelay == 0.0)
                        invalidValueDelay = _latestVector.TimeAfter((long)(_config.InvalidValueDelay * 1000));
                    else if (_latestVector.Reached(invalidValueDelay))
                        value = _config.DefaultInvalidValue;
                }

                bool BoardsConnected(int index)
                {
                    foreach (var stateIndex in _indexes.boardStates[index])
                        if (_latestVector[stateIndex] != (int)BaseSensorBox.ConnectionState.ReceivingValues)
                            return false;

                    return true;
                }
            }

            public class Indexes
            {
                public int value { get; internal set; } = -1;
                public int invalidValueDelay { get; internal set; } = -1;
                public int[] sensors { get; init; }
                public int[][] boardStates { get; init; }

                public Indexes(CA.LoopControlPluginBase.VectorDescription desc, Config _config)
                {
                    sensors = new int[_config.Sensors.Count];
                    boardStates = _config.SensorBoardStates.Select(s => new int[s.Count]).ToArray();
                    Array.Fill(sensors, -1);
                    for (int i = 0; i < boardStates.Length; i++)
                        Array.Fill(boardStates[i], -1);

                    for (int i = 0; i < desc.Count; i++)
                    {
                        var field = desc[i];
                        if (field == $"{_config.Name}")
                            value = i;
                        if (field == $"{_config.Name}_invalidValueDelay")
                            invalidValueDelay = i;
                        for (int j = 0; j < sensors.Length; j++)
                            if (field == _config.Sensors[j])
                                sensors[j] = i;
                        for (int j = 0; j < boardStates.Length; j++)
                            for (int k = 0; k < boardStates[j].Length; k++)
                                if (field == _config.SensorBoardStates[j][k])
                                    boardStates[j][k] = i;
                    }

                    if (value == -1) throw new ArgumentException($"Field used by '{_config.Name}' is not in the vector description: {_config.Name}_target", nameof(desc));
                    if (invalidValueDelay == -1 && _config.InvalidValueDelay > 0) throw new ArgumentException($"Field used by '{_config.Name}' is not in the vector description: {_config.Name}_invalidValueDelay", nameof(desc));
                    var missingIndex = Array.IndexOf(sensors, -1);
                    if (missingIndex >= 0) throw new ArgumentException($"Field used by '{_config.Name}' is not in the vector description: {_config.Sensors[missingIndex]}", nameof(desc));
                    for (int i = 0; i < boardStates.Length; i++)
                    {
                        missingIndex = Array.IndexOf(boardStates, -1);
                        if (missingIndex >= 0) throw new ArgumentException($"Field used by '{_config.Name}' is not in the vector description: {_config.SensorBoardStates[i][missingIndex]}", nameof(desc));
                    }
                }
            }
#pragma warning restore IDE1006 // Naming Styles
        }
    }
}
