using CA_DataUploaderLib.IOconf;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CA_DataUploaderLib.Helpers
{
    public class VectorFilterAndMath
    {
        private readonly CALogLevel _logLevel;
        protected List<FilterSample> _values;
        protected MathVectorExpansion _mathVectorExpansion;
        public VectorFilterAndMath(VectorDescription vectorDescription)
        {
            _logLevel = IOconfFile.GetOutputLevel();
            _values = GetFilters(vectorDescription);
            vectorDescription._items.AddRange(_values.Select(m => new VectorDescriptionItem("double", m.Output.Name, DataTypeEnum.Input)));
            RemoveHiddenSources(vectorDescription._items, i => i.Descriptor);
            _mathVectorExpansion = new MathVectorExpansion(vectorDescription);
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

        public VectorDescription VectorDescription { get { return _mathVectorExpansion.VectorDescription; } }

        public List<SensorSample> Apply(List<SensorSample> vector)
        {
            foreach (var filter in _values)
            {
                filter.Input(vector);
                vector.Add(filter.Output);
            }

            RemoveHiddenSources(vector, i => i.Name);
            _mathVectorExpansion.Expand(vector);
            return vector;
        }
    }
}
