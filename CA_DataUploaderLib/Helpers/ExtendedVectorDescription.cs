#nullable enable
using CA_DataUploaderLib.IOconf;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CA_DataUploaderLib.Helpers
{
    /// <summary>adds filters and math, sets outputs at the end of the vector</summary>
    public class ExtendedVectorDescription
    {
        private readonly CALogLevel _logLevel;
        private readonly List<FilterSample> _values;
        private readonly MathVectorExpansion _mathVectorExpansion;
        private readonly int _outputCount;
        public VectorDescription VectorDescription { get; }
        public int InputsAndFiltersCount { get; }
        /// <summary>
        /// list of inputs descriptions per node in the same order that <see cref="CommandHandler.GetNodeInputs"/> uses.
        /// </summary>
        public IReadOnlyList<(IOconfNode, IReadOnlyList<VectorDescriptionItem>)> InputsPerNode { get; set; }

        public ExtendedVectorDescription(List<(IOconfNode, IReadOnlyList<VectorDescriptionItem> values)> inputsPerNode, List<VectorDescriptionItem> globalInputs, List<VectorDescriptionItem> outputs)
        {
            _logLevel = IOconfFile.GetOutputLevel();
            InputsPerNode = inputsPerNode;
            List<VectorDescriptionItem> allItems = inputsPerNode.SelectMany(n => n.values).Concat(globalInputs).ToList();
            _values = GetFilters(allItems);
            allItems.AddRange(_values.Select(m => new VectorDescriptionItem("double", m.Output.Name, DataTypeEnum.Input)));
            RemoveHiddenSources(allItems, i => i.Descriptor);
            InputsAndFiltersCount = allItems.Count;
            _mathVectorExpansion = new MathVectorExpansion();
            allItems.AddRange(_mathVectorExpansion.GetVectorDescriptionEntries());
            allItems.AddRange(outputs);
            _outputCount = outputs.Count;
            _mathVectorExpansion.Initialize(allItems.Select(i => i.Descriptor));
            var duplicates = allItems.GroupBy(x => x.Descriptor).Where(x => x.Count() > 1).Select(x => x.Key);
            if (duplicates.Any())
                throw new Exception("Title of datapoint in vector was listed twice: " + string.Join(", ", duplicates));
            VectorDescription = new VectorDescription(allItems, RpiVersion.GetHardware(), RpiVersion.GetSoftware());
        }

        public int GetIndex(VectorDescriptionItem item) { return VectorDescription._items.IndexOf(item); }

        private static List<FilterSample> GetFilters(List<VectorDescriptionItem> inputs)
        {
            var filters = IOconfFile.GetFilters().ToList();
            var filtersWithoutItem = filters.SelectMany(f => f.SourceNames.Select(s => new { Filter = f, Source = s })).Where(f => !inputs.Any(i => i.Descriptor == f.Source)).ToList();
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

        /// <summary>applies math to the vector, assuming the inputs and filters were already applied using <see cref="ApplyInputsAndFilters(List{SensorSample}, DataVector)"/></summary>
        public void ApplyMath(DataVector vector) => _mathVectorExpansion.Apply(vector.Data);
        /// <summary>applies the inputs to the vector, transforming them first with the configured filters</summary>
        public void ApplyInputsAndFilters(List<SensorSample> inputs, DataVector vector)
        {
            //removing allocations here is tricky, but we should consider keeping the original inputs in the full vector and when hiding sources we set those inputs as not uploadable.
            //note that it also helps cross checking full vectors, if one also has: the previous vector, the state of filters (they have a queue of values within filter length), events in the cycle.
            foreach (var filter in _values)
            {
                filter.Input(inputs);
                inputs.Add(filter.Output);
            }

            RemoveHiddenSources(inputs, i => i.Name);
            var expectedInputsCount = VectorDescription.Length - _mathVectorExpansion.Count - _outputCount;
            if (inputs.Count != expectedInputsCount)
                throw new ArgumentException($"wrong input vector length (input, expected): {inputs.Count} <> {expectedInputsCount}");
            var data = vector.Data;
            for (int i = 0; i < inputs.Count; i++)
                data[i] = inputs[i].Value;
        }
    }
}
