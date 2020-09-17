using CA_DataUploaderLib.Extensions;
using CA_DataUploaderLib.IOconf;
using System.Collections.Generic;
using System.Linq;

namespace CA_DataUploaderLib.Helpers
{
    public class FilterUtil
    {
        protected List<FilterSample> _values;
        public FilterUtil()
        {
            _values = IOconfFile.GetFilters().Select(x => new FilterSample(x)).ToList();
            if (!_values.Any())
                return;

        }

        public List<double> FilterAndMath(List<double> vector)
        {
            // foreach(var filter in _values)


            return new List<double>();
        }
    }
}
