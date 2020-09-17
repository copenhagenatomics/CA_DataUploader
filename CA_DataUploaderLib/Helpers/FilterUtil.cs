using CA_DataUploaderLib.Extensions;
using CA_DataUploaderLib.IOconf;
using System.Collections.Generic;
using System.Linq;

namespace CA_DataUploaderLib.Helpers
{
    public class FilterUtil
    {
        protected List<IOconfInput> _config;
        protected double _filterLength;
        public FilterUtil(double filterLength)  // in seconds
        {
            _config = IOconfFile.GetFilters().Cast<IOconfInput>().IsInitialized().ToList();
            if (!_config.Any())
                return;

            _filterLength = filterLength;

        }

        public List<double> FilterAndMath(List<double> vector)
        {
            // for each of the value which need to be filtered send it through the filter
            // Call FilterSample.Value
            return vector;
        }
    }
}
