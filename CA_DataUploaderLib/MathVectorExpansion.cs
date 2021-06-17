using CA_DataUploaderLib.IOconf;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CA_DataUploaderLib
{
    public class MathVectorExpansion
    {
        private readonly List<IOconfMath> _mathStatements;

        public MathVectorExpansion() : this(IOconfFile.GetMath) { }
        public MathVectorExpansion(Func<IEnumerable<IOconfMath>> getMath)
        {
            _mathStatements = getMath().ToList();
        }

        public IEnumerable<VectorDescriptionItem> GetVectorDescriptionEntries() => 
            _mathStatements.Select(m => new VectorDescriptionItem("double", m.Name, DataTypeEnum.State));

        /// <param name="inputVector">the vector to expand.</param>
        /// <remarks>
        /// The math statements are appended at the end of the vector in the order returned by <see cref="GetVectorDescriptionEntries()" />.
        /// This method and class does not track changes to the <see cref="IOconfFile"/>, use a new instance if needed.
        /// </remarks>
        public void Expand(List<SensorSample> inputVector)
        {
            var dic = GetVectorDictionary(inputVector);
            foreach (var math in _mathStatements)
            {
                inputVector.Add(math.Calculate(dic));
            }
        }

        private Dictionary<string, object> GetVectorDictionary(List<SensorSample> vector) => vector.ToDictionary(s => s.Name, s => (object)s.Value);
    }
}
