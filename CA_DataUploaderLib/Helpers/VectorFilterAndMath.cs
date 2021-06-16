using CA_DataUploaderLib.IOconf;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CA_DataUploaderLib.Helpers
{
    /// <summary>adds filters and maths, while moving all outputs to the end of the vector</summary>
    public class VectorFilterAndMath
    {
        private readonly CALogLevel _logLevel;
        private List<FilterSample> _values;
        private MathVectorExpansion _mathVectorExpansion;
        private int _outputCount;
        public VectorDescription VectorDescription { get; }

        public VectorFilterAndMath(VectorDescription vectorDescription)
        {
            _logLevel = IOconfFile.GetOutputLevel();
            VectorDescription = vectorDescription;
            _values = GetFilters(vectorDescription);
            vectorDescription._items.AddRange(_values.Select(m => new VectorDescriptionItem("double", m.Output.Name, DataTypeEnum.Input)));
            RemoveHiddenSources(vectorDescription._items, i => i.Descriptor);
            _mathVectorExpansion = new MathVectorExpansion();
            VectorDescription._items.AddRange(_mathVectorExpansion.GetVectorDescriptionEntries());
            _outputCount = MoveOutputsToTheEnd(vectorDescription._items);
        }

        private int MoveOutputsToTheEnd(List<VectorDescriptionItem> items)
        {
            var outputs = items.Where(i => i.DirectionType == DataTypeEnum.Output).ToList();
            items.RemoveAll(i => i.DirectionType == DataTypeEnum.Output);
            items.AddRange(outputs);
            return outputs.Count;
        }

        private static List<FilterSample> GetFilters(VectorDescription vectorDesc)
        {
            var filters = IOconfFile.GetFilters().ToList();
            var filtersWithoutItem = filters.SelectMany(f => f.SourceNames.Select(s => new { Filter = f, Source = s })).Where(f => !vectorDesc.HasItem(f.Source)).ToList();
            foreach (var filter in filtersWithoutItem)
                CALog.LogErrorAndConsoleLn(LogID.A, $"ERROR in {Directory.GetCurrentDirectory()}\\IO.conf:{Environment.NewLine} Filter: {filter.Filter.Name} points to missing sensor: {filter.Source}");
            if (filtersWithoutItem.Count > 0)
                throw new InvalidOperationException("Misconfigured filters detected");
            return filters.Select(x => new FilterSample(x)).ToList();
        }

        private void RemoveHiddenSources<T>(List<T> list, Func<T, string> getEntryName)
        {
            if (_logLevel == CALogLevel.Debug)
                return;

            foreach (var filter in _values)
            {
                if (!filter.Filter.HideSource) continue;
                list.RemoveAll(vd => filter.HasSource(getEntryName(vd)));
            }
        }

        /// <remarks>the input vector is expected to contains all input + state initially sent in the vector description passed to the constructor.</remarks>
        public List<SensorSample> Apply(List<SensorSample> inputVector)
        {
            foreach (var filter in _values)
            {
                filter.Input(inputVector);
                inputVector.Add(filter.Output);
            }

            RemoveHiddenSources(inputVector, i => i.Name);
            _mathVectorExpansion.Expand(inputVector);
            if (inputVector.Count + _outputCount != VectorDescription.Length)
                throw new ArgumentException($"wrong input vector length (input, expected): {inputVector.Count} <> {VectorDescription.Length - _outputCount}");
            return inputVector;
        }

        /// <summary>appends the specified outputs to the end of the input vector</summary>
        /// <remarks>the input vector is expected to be the expanded version returned by the <see cref="Apply" /> method</remarks>
        public void AddOutputsToInputVector(List<SensorSample> expandedInputVector, IEnumerable<SensorSample> outputs) => expandedInputVector.AddRange(outputs);
    }
}
