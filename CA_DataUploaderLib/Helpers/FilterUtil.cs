using CA_DataUploaderLib;
using CA_DataUploaderLib.IOconf;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CA_DataUploaderLib.Helpers
{
    public class FilterUtil : IDisposable
    {
        protected List<FilterSample> _values;
        protected MathVectorExpansion _mathVectorExpansion;
        public FilterUtil(VectorDescription vectorDescription)
        {
            _mathVectorExpansion = new MathVectorExpansion(vectorDescription);

            _values = IOconfFile.GetFilters().Select(x => new FilterSample(x)).ToList();
            if (!_values.Any())
                return;

        }

        public VectorDescription ExpandedVectorDescription { get { return _mathVectorExpansion.VectorDescription; } }

        public List<double> FilterAndMath(List<SensorSample> vector)
        {
            _mathVectorExpansion.Expand(vector);

            foreach (var filter in _values)
            {
                filter.Input(vector);
                vector.Add(filter.Output);
            }

            return vector.Select(x => x.Value).ToList();
        }

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                disposedValue = true;
            }
        }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
        }

        #endregion
    }
}
