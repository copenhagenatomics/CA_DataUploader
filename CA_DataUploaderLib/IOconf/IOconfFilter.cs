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
        private readonly FilterType _filterType;
        private readonly double _filterLength;
        private LegacyFilter? _legacyFilter;
        private DecisionFilter? _decisionFilter;

        public string NameInVector { get; }
        public List<string> SourceNames { get; private set; } = [];

        public IOconfFilter(string row, int lineNum) : base(row, lineNum, "Filter")
        {
            Format = $"Filter;Name;Sustained;FilterLength;conditionExpression{Environment.NewLine}Filter;Name;Average/Max/Min/SumAvg/DiffAvg/Triangle;FilterLength;SourceNames;[hidesource]";
            ExpandsTags = true;

            var list = ToList();
            if (list.Count < 5)
                throw new FormatException($"Wrong filter format: {row}{Environment.NewLine}{Format}");

            if (!Enum.TryParse(list[2], out _filterType))
                throw new FormatException($"Wrong filter type: {row}{Environment.NewLine}{Format}");

            if (!list[3].TryToDouble(out _filterLength))
                throw new FormatException($"Wrong filter length: {row}{Environment.NewLine}{Format}");

            NameInVector = _filterType == FilterType.Sustained ? Name : Name + "_filter";
        }

        public override void ValidateDependencies(IIOconf ioconf)
        {
            var list = ToList();
            if (_filterType == FilterType.Sustained)
            {
                if (list.Count != 5)
                    throw new FormatException($"Unexpected format for {_filterType} filter: {Row}{Environment.NewLine}{Format}");
                var decision = new SustainedDecisionFilter(Name, _filterLength, list[4], Row, Format);
                (_decisionFilter, SourceNames) = (decision, decision.SourceNames);
            }
            else
            {
                var sources = list.Skip(4).ToList();
                var hideSource = sources.Last() == "hidesource";
                if (hideSource)
                    sources.Remove("hidesource");
                (_legacyFilter, SourceNames) = (new(NameInVector, _filterType, _filterLength, sources, hideSource), sources);
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
                    throw new FormatException($"Wrong format for filter expression: {row}{Environment.NewLine}{format}", ex);
                }
            }

            public override IEnumerable<string> GetDecisionFields() => [_name, _name + "_targettime"];
            public override void MakeDecision(MathVectorExpansion.MathContext context)
            {
                var (time, targetTime) = (context.Timestamp, context[_targetTimeFieldIndex]);

                if (!context.CalculateBoolean(_sustainedExpression))
                    (context[_outputFieldIndex], context[_targetTimeFieldIndex]) = (0, 0);//the condition is not met, reset both the filter output and target time
                else if (targetTime == 0)
                    (context[_outputFieldIndex], context[_targetTimeFieldIndex]) = (0, time.AddSeconds(_length).ToOADate());//the first cycle meeting the condition sets the target time in filterLength seconds
                else
                    context[_outputFieldIndex] = time >= DateTime.FromOADate(targetTime) ? 1 : 0; //set the output to 1 if we already reached the target time
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
