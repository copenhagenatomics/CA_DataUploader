using CA_DataUploaderLib;
using CA_DataUploaderLib.IOconf;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CA_DataUploaderLib.Helpers
{
    public class VectorFilterAndMath : IDisposable
    {
        protected List<FilterSample> _values;
        protected MathVectorExpansion _mathVectorExpansion;
        public VectorFilterAndMath(VectorDescription vectorDescription)
        {
            _values = IOconfFile.GetFilters().Select(x => new FilterSample(x)).ToList();
            if (_values.Any())
            {
                vectorDescription._items.AddRange(_values.Select(m => new VectorDescriptionItem("double", m.Filter.Name, DataTypeEnum.Input)));
            }
            
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
