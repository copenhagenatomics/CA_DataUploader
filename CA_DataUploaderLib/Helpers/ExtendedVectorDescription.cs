#nullable enable
using CA_DataUploaderLib.IOconf;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CA_DataUploaderLib.Helpers
{
    /// <summary>adds filters and math, sets outputs at the end of the vector</summary>
    public class ExtendedVectorDescription
    {
        private readonly MathVectorExpansion _mathVectorExpansion;
        private readonly FilterVectorExpansion _filterVectorExpansion;
        private readonly int _inputsCount;

        public VectorDescription VectorDescription { get; }
        /// <summary>
        /// list of inputs descriptions per node in the same order that <see cref="CommandHandler.GetNodeInputs"/> uses.
        /// </summary>
        public IReadOnlyList<(IOconfNode, IReadOnlyList<VectorDescriptionItem>)> InputsPerNode { get; set; }

        public ExtendedVectorDescription(IIOconf ioconf, List<(IOconfNode, IReadOnlyList<VectorDescriptionItem> values)> inputsPerNode, List<VectorDescriptionItem> globalInputs, List<VectorDescriptionItem> outputs)
        {
            InputsPerNode = inputsPerNode;
            List<VectorDescriptionItem> allItems = inputsPerNode.SelectMany(n => n.values).Concat(globalInputs).ToList();
            _filterVectorExpansion = new FilterVectorExpansion(allItems, ioconf.GetFilters, ioconf.GetOutputLevel());
            _inputsCount = allItems.Count; //this count includes legacy filters and excludes hidden sources

            _mathVectorExpansion = new MathVectorExpansion(ioconf.GetMath);
            allItems.AddRange(_filterVectorExpansion.GetDecisionVectorDescriptionEntries());
            allItems.AddRange(_mathVectorExpansion.GetVectorDescriptionEntries());
            allItems.AddRange(outputs);
            
            var duplicates = allItems.GroupBy(x => x.Descriptor, StringComparer.InvariantCultureIgnoreCase).Where(x => x.Count() > 1).Select(x => x.Key);
            if (duplicates.Any())
                throw new Exception("Different fields cannot use the same name (even if the casing is different). Please rename: " + string.Join(", ", duplicates));

            var allFields = allItems.Select(i => i.Descriptor).ToArray();
            _filterVectorExpansion.Initialize(allFields);
            _mathVectorExpansion.Initialize(allFields);
            VectorDescription = new VectorDescription(allItems, RpiVersion.GetHardware(), RpiVersion.GetSoftware());
        }

        public void MakeDecision(DataVector vector)
        {
            using var ctx = _mathVectorExpansion.NewContext(vector);
            _filterVectorExpansion.Apply(ctx);
            _mathVectorExpansion.Apply(ctx);
        }

        public void ApplyInputsTo(List<SensorSample> inputs, DataVector vector)
        {
            _filterVectorExpansion.ApplyLegacyFilters(inputs);
            if (inputs.Count != _inputsCount)
                throw new ArgumentException($"wrong input vector length (input, expected): {inputs.Count} <> {_inputsCount}");

            var data = vector.Data;
            for (int i = 0; i < inputs.Count; i++)
                data[i] = inputs[i].Value;
        }

        public void CopyInputsTo(DataVector newVector, DataVector vector)
        {
            var data = vector.Data;
            for (int i = 0; i < _inputsCount; i++)
                data[i] = newVector[i];
        }
    }
}
