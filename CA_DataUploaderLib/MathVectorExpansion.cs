using CA_DataUploaderLib.IOconf;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CA_DataUploaderLib
{
    public class MathVectorExpansion
    {
        public VectorDescription VectorDescription { get; private set; }
        private List<IOconfMath> _mathStatements;

        public MathVectorExpansion(VectorDescription vectorDescription) : this(vectorDescription, IOconfFile.GetMath) { }
        public MathVectorExpansion(VectorDescription vectorDescription, Func<IEnumerable<IOconfMath>> getMath)
        {
            VectorDescription = vectorDescription;
            _mathStatements = getMath().ToList();
            VectorDescription._items.AddRange(_mathStatements.Select(m => new VectorDescriptionItem("double", m.Name, DataTypeEnum.State)));
        }

        /// <param name="vector">The vector to expand.</param>
        /// <remarks>
        /// The vector must match the order and amount of entries specified in <see cref="MathVectorExpansion(VectorDescription)"/>.
        /// This method and class does not track changes to the <see cref="IOconfFile"/>, use a new instance if needed.
        /// </remarks>
        public void Expand(List<SensorSample> vector)
        {
            if (vector.Count() + _mathStatements.Count() != VectorDescription.Length)
                throw new ArgumentException($"wrong vector length (input, expected): {vector.Count} <> {VectorDescription.Length - _mathStatements.Count()}");

            var dic = GetVectorDictionary(vector);
            foreach (var math in _mathStatements)
            {
                vector.Add(math.Calculate(dic));
            }
        }

        private Dictionary<string, object> GetVectorDictionary(List<SensorSample> vector)
        {
            var dic = new Dictionary<string, object>(VectorDescription.Length);
            for(int i = 0;i<vector.Count;i++)            
            {
                dic.Add(VectorDescription._items[i].Descriptor, vector[i].Value);
            }

            return dic;
        }
    }
}
