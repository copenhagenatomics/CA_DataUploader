#nullable enable
using CA_DataUploaderLib.Helpers;
using CA_DataUploaderLib.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using NCalc.Domain;

namespace CA_DataUploaderLib.IOconf
{
    public enum FilterType
    {
        None = 0,
        Average = 1,
        Max = 2,
        Min = 3,
        SumAvg = 4,
        DiffAvg = 5,
        Triangle = 6,
        Sustained = 7,
    }

    public class IOconfFilter : IOconfRow
    {
        private readonly LegacyFilter? _legacyFilter;
        private readonly DecisionFilter? _decisionFilter;

        public string NameInVector { get; }
        public List<string> SourceNames { get; }

        public IOconfFilter(string row, int lineNum) : base(row, lineNum, "Filter")
        {
            Format = $"Filter;Name;Sustained;FilterLength;conditionExpression{Environment.NewLine}Filter;Name;Average/Max/Min/SumAvg/DiffAvg/Triangle;FilterLength;SourceNames;[hidesource]";

            var list = ToList();
            if (list.Count < 5)
                throw new Exception($"Wrong filter format: {row}{Environment.NewLine}{Format}");

            if (!Enum.TryParse(list[2], out FilterType filterType))
                throw new Exception($"Wrong filter type: {row}{Environment.NewLine}{Format}");

            if (!list[3].TryToDouble(out var filterLength))
                throw new Exception($"Wrong filter length: {row}{Environment.NewLine}{Format}");

            if (filterType == FilterType.Sustained)
                (_decisionFilter, NameInVector, SourceNames) = InitSustainedFilter(list);
            else
                (_legacyFilter, NameInVector, SourceNames) = InitLegacyFilter(list);

            (SustainedDecisionFilter, string nameInVector, List<string>) InitSustainedFilter(List<string> list)
            {
                if (list.Count != 5)
                    throw new FormatException($"Unexpected format for {filterType} filter: {Row}{Environment.NewLine}{Format}");
                var decision = new SustainedDecisionFilter(Name, filterLength, list[4], Row, Format);
                return (decision, Name, decision.SourceNames);
            }

            (LegacyFilter, string nameInVector, List<string>) InitLegacyFilter(List<string> list)
            {
                var sources = list.Skip(4).ToList();
                var hideSource = sources.Last() == "hidesource";
                if (hideSource)
                    sources.Remove("hidesource");
                var name = Name + "_filter";
                return (new(name, filterType, filterLength, sources, hideSource), name, sources);
            }
        }

        public static List<LegacyFilter> LegacyFilters(IEnumerable<IOconfFilter> filters) => filters.Select(f => f._legacyFilter).OfType<LegacyFilter>().ToList();
        public static List<DecisionFilter> DecisionFilters(IEnumerable<IOconfFilter> filters) => filters.Select(f => f._decisionFilter).OfType<DecisionFilter>().ToList();

        public override IEnumerable<string> GetExpandedSensorNames()
        {
            yield return NameInVector;
        }

        public abstract class DecisionFilter
        {
            public abstract IEnumerable<string> GetDecisionFields();
            public abstract void Initialize(List<string> fields);
            public abstract void MakeDecision(MathVectorExpansion.MathContext context);
        }

        public class SustainedDecisionFilter : DecisionFilter
        {
            private readonly string _name;
            private readonly string _row;
            private readonly double _length;
            private readonly LogicalExpression _sustainedExpression;
            private int _outputFieldIndex = -1;
            private int _targetTimeFieldIndex = -1;
            public List<string> SourceNames;

            public SustainedDecisionFilter(string name, double length, string expression, string row, string format)
            {
                try
                {
                    _name = name;
                    _length = length;
                    _row = row;
                    (_sustainedExpression, SourceNames) = IOconfMath.CompileExpression(expression);
                    // Perform test calculation using default input values
                    if (IOconfMath.Calculate(SourceNames.ToDictionary(s => s, s => (object)0), _sustainedExpression) is not bool)
                        throw new FormatException($"Only boolean filter expressions are supported for Sustained filters: {row}{Environment.NewLine}{format}");
                }
                catch (OverflowException ex)
                {
                    throw new OverflowException($"Filter expression causes integer overflow: {row}{Environment.NewLine}{format}", ex);
                }
                catch (FormatException)
                { 
                    throw;
                }
                catch (Exception ex)
                {
                    throw new Exception($"Wrong format for filter expression: {row}{Environment.NewLine}{format}", ex);
                }
            }

            public override IEnumerable<string> GetDecisionFields() => [_name, _name + "_targettime"];
            public override void MakeDecision(MathVectorExpansion.MathContext context)
            {
                var (time, vector) = (context.Vector.Timestamp, context.Vector.Data);
                ref var targetTime = ref vector[_targetTimeFieldIndex];
                ref var output = ref vector[_outputFieldIndex];

                if (!context.CalculateBoolean(_sustainedExpression))
                    (output, targetTime) = (0, 0);//the condition is not met, reset both the filter output and target time
                else if (targetTime == 0)
                    (output, targetTime) = (0, time.AddSeconds(_length).ToOADate());//the first cycle meeting the condition sets the target time in filterLength seconds
                else
                    output = time >= DateTime.FromOADate(targetTime) ? 1 : 0; //set the output to 1 if we already reached the target time
            }

            public override void Initialize(List<string> fields)
            {
                _outputFieldIndex = fields.IndexOf(_name);
                if (_outputFieldIndex < 0) throw new ArgumentException($"{_name} was not found in received vector fields", nameof(fields));
                _targetTimeFieldIndex = fields.IndexOf(_name + "_targettime");
                if (_targetTimeFieldIndex < 0) throw new ArgumentException($"{_name} was not found in received vector fields", nameof(fields));
                var missingSources = SourceNames.Where(s => !fields.Contains(s)).ToList();
                if (missingSources.Count > 0)
                    throw new FormatException($"Filter {_name} uses sources {string.Join(',', missingSources)} which were not found in received vector fields. Row: {_row}");
            }
        }
    }
}
