﻿#nullable enable
using CA_DataUploaderLib.IOconf;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CA_DataUploaderLib
{
    public class MathVectorExpansion
    {
        private readonly List<IOconfMath> _mathStatements;
        private readonly Dictionary<string, object> _reusableFiedsDictionary = new();
        private readonly Dictionary<int, string> _fieldsByIndex = new();
        private readonly List<(IOconfMath math, int mathFieldIndex)> _mathWithFieldIndexes = new();

        public MathVectorExpansion() : this(IOconfFile.GetMath) { }
        public MathVectorExpansion(Func<IEnumerable<IOconfMath>> getMath) => _mathStatements = getMath().ToList();
        public int Count => _mathStatements.Count;
        public IEnumerable<VectorDescriptionItem> GetVectorDescriptionEntries() => _mathStatements.Select(m => new VectorDescriptionItem("double", m.Name, DataTypeEnum.State));
        /// <param name="vectorFields">all the vector fields, including those returned by <see cref="GetVectorDescriptionEntries"/></param>
        public void Initialize(IEnumerable<string> vectorFields)
        {
            var fields = vectorFields.ToList();
            int fieldIndex = 0;
            fields.ForEach(f => _fieldsByIndex.Add(fieldIndex++, f));
            fields.ForEach(f => _reusableFiedsDictionary.Add(f, 0d));
            foreach (var math in _mathStatements)
            {
                var index = fields.IndexOf(math.Name);
                if (index < 0) throw new ArgumentException($"{math.Name} was not found in received vector fields", nameof(vectorFields));
                _mathWithFieldIndexes.Add((math, index));
            }
        }

        /// <param name="vector">the full vector with the same fields as specified in <see cref="Initialize"/> (with only the inputs updated in this cycle)</param>
        public void Apply(Span<double> vector)
        {
            if (_fieldsByIndex == null || _reusableFiedsDictionary == null) throw new InvalidOperationException("Initialize has not been called before calling Apply");
            if (vector.Length != _fieldsByIndex.Count)
                throw new ArgumentException($"the specified vector length does not match the fields received during initialize - {vector.Length} vs {_fieldsByIndex.Count}", nameof(vector));

            //note that not only we have all the innecesary copying of data here, but even though the dictionary its reused we have boxing of the values (IOConfMath.Calculate requires a Dictionary<string, object>.
            //one option to improve both is to have a different math expression engine that allows preprocessing the expression in a way that allows to specify the parameters by index
            //and allows to specify them in a single desired type. Also note the engine also causes boxing of the result as it returns an object we convert to a double.
            for (int i = 0; i < _fieldsByIndex.Count; i++)
                _reusableFiedsDictionary[_fieldsByIndex[i]] = vector[i];

            foreach (var (math, index) in _mathWithFieldIndexes)
                vector[index] = math.Calculate(_reusableFiedsDictionary);
        }
    }
}
