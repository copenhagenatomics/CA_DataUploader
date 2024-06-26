#nullable enable
using CA_DataUploaderLib.Helpers;
using CA_DataUploaderLib.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using NCalc.Domain;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfFilter : IOconfRow
    {
        public readonly FilterType filterType;
        public readonly double filterLength;  // in seconds. 
        private readonly LogicalExpression? _sustainedExpression;
        private int _decisionFieldIndex = -1;
        private int _sustainedTargetTimeFieldIndex = -1;

        //TODO: legacy vs. decision uses different type of sources, name in vector, etcvalidation
        public string NameInVector => filterType == FilterType.Sustained ? Name : Name + "_filter";
        public List<string> SourceNames { get; }
        public bool HideSource { get; }
        public bool IsDecisionFilter => filterType == FilterType.Sustained;

        public IOconfFilter(string row, int lineNum) : base(row, lineNum, "Filter")
        {
            Format = $"Filter;Name;Sustained;FilterLength;conditionExpression{Environment.NewLine}Filter;Name;Average/Max/Min/SumAvg/DiffAvg/Triangle;FilterLength;SourceNames;[hidesource]";

            var list = ToList();
            if (!Enum.TryParse(list[2], out filterType))
                throw new Exception($"Wrong filter type: {row} {Environment.NewLine}{Format}");

            if (!list[3].TryToDouble(out filterLength))
                throw new Exception($"Wrong filter length: {row} {Environment.NewLine}{Format}");

            if (!IsDecisionFilter)
                (SourceNames, HideSource) = InitLegacyFilter(list);
            else if (list.Count != 5)
                throw new FormatException($"Unexpected format for {filterType} filter: {Row}{Environment.NewLine}{Format}");
            else
                (_sustainedExpression, SourceNames) = InitSustainedFilter(list, Row, Format);
        }

        private (List<string> sources, bool hideSource) InitLegacyFilter(List<string> list)
        {
            var hideSource = false;
            var sources = list.Skip(4).ToList();
            if (!IsDecisionFilter && sources.Last() == "hidesource")
            {
                hideSource = true;
                sources.Remove("hidesource");
            }

            // source validation happens later, as there is not a 1-1 relation of IOConfFile entries and values getting into the Vector i.e. some oxygen sensors have 3 values. 
            return (sources, hideSource);
        }

        private static (LogicalExpression sustainedExpression, List<string> sourceNames) InitSustainedFilter(List<string> list, string row, string format)
        {
            try
            {
                var (sustainedExpression, sourceNames) = IOconfMath.CompileExpression(list[4]);
                // Perform test calculation using default input values
                if (IOconfMath.Calculate(sourceNames.ToDictionary(s => s, s => (object)0), sustainedExpression) is not bool)
                    throw new FormatException($"Only boolean filter expressions are supported: {row}{Environment.NewLine}{format}");
                return (sustainedExpression, sourceNames);
            }
            catch (OverflowException ex)
            {
                throw new OverflowException($"Filter expression causes integer overflow: {row}{Environment.NewLine}{format}", ex);
            }
            catch (FormatException) { throw; }
            catch (Exception ex)
            {
                throw new Exception($"Wrong format for filter expression: {row}{Environment.NewLine}{format}", ex);
            }
        }

        public override IEnumerable<string> GetExpandedSensorNames(IIOconf ioconf)
        {
            yield return NameInVector;
        }

        public IEnumerable<string> GetDecisionFields() => 
            filterType == FilterType.Sustained 
                ? [NameInVector, NameInVector + "_targettime"]
                : throw new InvalidOperationException($"Unexpected call to GetDecisionFields for filter type {Row}");

        public void MakeDecision(MathVectorExpansion.MathContext context)
        {
            if (filterType != FilterType.Sustained) 
                throw new InvalidOperationException($"Unexpected MakeDecision call for non decision filter: {Row}");

            MakeDecisionSustained(context);
        }

        private void MakeDecisionSustained(MathVectorExpansion.MathContext context)
        {
            var (time, vector) = (context.Vector.Timestamp, context.Vector.Data);
            var expr = _sustainedExpression ?? throw new InvalidOperationException($"Unexpected null expression for filter: {Row}");
            ref var targetTime = ref vector[_sustainedTargetTimeFieldIndex];
            ref var output = ref vector[_decisionFieldIndex];

            if (!context.CalculateBoolean(expr))
                (output, targetTime) = (0, 0);//the condition is not met, reset both the filter output and target time
            else if (targetTime == 0)
                (output, targetTime) = (0, time.AddSeconds(filterLength).ToOADate());//the first cycle meeting the condition sets the target time in filterLength seconds
            else
                output = time >= DateTime.FromOADate(targetTime) ? 1 : 0; //set the output to 1 if we already reached the target time
        }

        public void Initialize(List<string> fields)
        {
            if (filterType != FilterType.Sustained) 
                throw new InvalidOperationException($"Unexpected Initialize call for non decision filter: {Row}");

            _decisionFieldIndex = fields.IndexOf(Name);
            if (_decisionFieldIndex < 0) throw new ArgumentException($"{Name} was not found in received vector fields", nameof(fields));
            _sustainedTargetTimeFieldIndex = fields.IndexOf(Name + "_targettime");
            if (_sustainedTargetTimeFieldIndex < 0) throw new ArgumentException($"{Name} was not found in received vector fields", nameof(fields));
            foreach (var source in SourceNames)
            {
                if (!fields.Contains(source))
                    throw new ArgumentException($"Filter {Name} uses {source} which was not found in received vector fields", nameof(fields));
            }
        }
    }
}
