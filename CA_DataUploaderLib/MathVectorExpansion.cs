using CA_DataUploaderLib.IOconf;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CA_DataUploaderLib
{
    public class MathVectorExpansion
    {
        private VectorDescription _vectorDescriptionWithoutMath;
        public VectorDescription VectorDescription { get; }
        private List<IOconfMath> _mathStatements;

        public MathVectorExpansion(VectorDescription vectorDescription) : this(vectorDescription, IOconfFile.GetMath) { }
        public MathVectorExpansion(VectorDescription vectorDescription, Func<IEnumerable<IOconfMath>> getMath)
        {
            _vectorDescriptionWithoutMath = vectorDescription;
            _mathStatements = getMath().ToList();
            VectorDescription = vectorDescription.WithExtraItems(_mathStatements.Select(m => new VectorDescriptionItem("double", m.Name, DataTypeEnum.State)));
        }

        /// <param name="vector">The vector to expand.</param>
        /// <remarks>
        /// The vector must match the order and amount of entries specified in <see cref="MathVectorExpansion(VectorDescription)"/>.
        /// This method and class does not track changes to the <see cref="IOconfFile"/>, use a new instance if needed.
        /// </remarks>
        public void Expand(List<double> vector)
        {
            if (vector.Count() != _vectorDescriptionWithoutMath.Length)
                throw new ArgumentException($"wrong vector length (input, expected): {vector.Count} <> {_vectorDescriptionWithoutMath.Length}");

            var dic = GetVectorDictionary(vector);
            foreach (var math in _mathStatements)
            {
                vector.Add(math.Calculate(dic));
            }
        }

        private Dictionary<string, object> GetVectorDictionary(List<double> vector)
        {
            var dic = new Dictionary<string, object>(_vectorDescriptionWithoutMath._items.Count);
            int i = 0;
            foreach (var item in _vectorDescriptionWithoutMath._items)
            {
                dic.Add(item.Descriptor, vector[i++]);
            }

            return dic;
        }
    }
}
