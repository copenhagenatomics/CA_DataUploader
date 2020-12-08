using CA_DataUploaderLib.IOconf;
using System;
using System.Collections.Generic;
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
            _values = IOconfFile.GetFilters().Select(x => new FilterSample(x)).ToList();
            vectorDescription._items.AddRange(_values.Select(m => new VectorDescriptionItem("double", m.Filter.Name, DataTypeEnum.Input)));
            RemoveHiddenSources(vectorDescription._items, i => i.Descriptor);
            _mathVectorExpansion = new MathVectorExpansion(vectorDescription);
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

        public List<double> Apply(List<SensorSample> vector)
        {
            foreach (var filter in _values)
            {
                filter.Input(vector);
                vector.Add(filter.Output);
            }

            RemoveHiddenSources(vector, i => i.Name);
            _mathVectorExpansion.Expand(vector);
            return vector.Select(x => x.Value).ToList();
        }
    }
}
