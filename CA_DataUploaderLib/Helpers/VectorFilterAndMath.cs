using CA_DataUploaderLib.IOconf;
using System.Collections.Generic;
using System.Linq;

namespace CA_DataUploaderLib.Helpers
{
    public class VectorFilterAndMath
    {
        protected List<FilterSample> _values;
        protected MathVectorExpansion _mathVectorExpansion;
        public VectorFilterAndMath(VectorDescription vectorDescription)
        {
            _values = IOconfFile.GetFilters().Select(x => new FilterSample(x)).ToList();
            vectorDescription._items.AddRange(_values.Select(m => new VectorDescriptionItem("double", m.Filter.Name, DataTypeEnum.Input)));            
            _mathVectorExpansion = new MathVectorExpansion(vectorDescription);
        }

        public VectorDescription VectorDescription { get { return _mathVectorExpansion.VectorDescription; } }

        public List<double> Apply(List<SensorSample> vector)
        {
            foreach (var filter in _values)
            {
                filter.Input(vector);
                vector.Add(filter.Output);
            }

            _mathVectorExpansion.Expand(vector);
            return vector.Select(x => x.Value).ToList();
        }
    }
}
