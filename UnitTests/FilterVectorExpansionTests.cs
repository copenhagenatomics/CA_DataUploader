using CA_DataUploaderLib;
using CA_DataUploaderLib.IOconf;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace UnitTests
{
    [TestClass]
    public class FilterVectorExpansionTests
    {
        [TestMethod]
        public void InputsIncludeLegacyFilter()
        {
            var inputs = ToDescItemInputs(["MyName"]);
            var expansion = new FilterVectorExpansion(inputs, GetFiltersFunc("Filter;MyFilter;Triangle;3;MyName"), CALogLevel.Normal);
            Assert.AreEqual("MyName,MyFilter_filter", string.Join(',', inputs.Select(i => i.Descriptor)));
            Assert.AreEqual("", string.Join(',', expansion.GetDecisionVectorDescriptionEntries().Select(i => i.Descriptor)));
        }

        [TestMethod]
        public void VectorIncludesSustainedFilter()
        {
            var inputs = ToDescItemInputs(["MyName"]);
            var expansion = new FilterVectorExpansion(inputs, GetFiltersFunc("Filter;MyFilter;Sustained;3;MyName + 2 > 3"), CALogLevel.Normal);
            Assert.AreEqual("MyName", string.Join(',', inputs.Select(i => i.Descriptor)));
            Assert.AreEqual("MyFilter,MyFilter_targettime", string.Join(',', expansion.GetDecisionVectorDescriptionEntries().Select(i => i.Descriptor)));
        }

        [TestMethod]
        public void AppliesSustainedFilter()
        {
            var inputsDesc = ToDescItemInputs(["MyName"]);
            var expansion = new FilterVectorExpansion(inputsDesc, GetFiltersFunc("Filter;MyFilter;Sustained;3;MyName - 2 >= 3"), CALogLevel.Normal);
            var mathExpansion = new MathVectorExpansion(() => []);
            List<VectorDescriptionItem> allFields = [.. inputsDesc, .. expansion.GetDecisionVectorDescriptionEntries()];
            expansion.Initialize(allFields.Select(v => v.Descriptor));
            mathExpansion.Initialize(allFields.Select(v => v.Descriptor));
            var vector = new DataVector([0,0,0], DateTime.UtcNow);
            ApplyFilterCycleAndAssertExpectedValues(expansion, mathExpansion, ref vector, 0, 11, 0);
            ApplyFilterCycleAndAssertExpectedValues(expansion, mathExpansion, ref vector, 1, 6, 0);
            ApplyFilterCycleAndAssertExpectedValues(expansion, mathExpansion, ref vector, 1, 1, 0);
            ApplyFilterCycleAndAssertExpectedValues(expansion, mathExpansion, ref vector, 1, 6, 0);
            ApplyFilterCycleAndAssertExpectedValues(expansion, mathExpansion, ref vector, 1, 7, 0);
            ApplyFilterCycleAndAssertExpectedValues(expansion, mathExpansion, ref vector, 1, 6, 0);
            ApplyFilterCycleAndAssertExpectedValues(expansion, mathExpansion, ref vector, 1, 6, 1);
            ApplyFilterCycleAndAssertExpectedValues(expansion, mathExpansion, ref vector, 1, 5, 1);
            ApplyFilterCycleAndAssertExpectedValues(expansion, mathExpansion, ref vector, 1, 6, 1);
            ApplyFilterCycleAndAssertExpectedValues(expansion, mathExpansion, ref vector, 1, 5, 1);
            ApplyFilterCycleAndAssertExpectedValues(expansion, mathExpansion, ref vector, 1, 4, 0);
            ApplyFilterCycleAndAssertExpectedValues(expansion, mathExpansion, ref vector, 1, 5, 0);
            ApplyFilterCycleAndAssertExpectedValues(expansion, mathExpansion, ref vector, 1, 6, 0);
            ApplyFilterCycleAndAssertExpectedValues(expansion, mathExpansion, ref vector, 1, 5, 0);
            ApplyFilterCycleAndAssertExpectedValues(expansion, mathExpansion, ref vector, 1, 7, 1);
        }

        [TestMethod]
        public void AppliesSustainedFilterWithLessThanCondition()
        {
            var inputsDesc = ToDescItemInputs(["MyName"]);
            var expansion = new FilterVectorExpansion(inputsDesc, GetFiltersFunc("Filter;MyFilter;Sustained;3;3 <= MyName - 2"), CALogLevel.Normal);
            var mathExpansion = new MathVectorExpansion(() => []);
            List<VectorDescriptionItem> allFields = [.. inputsDesc, .. expansion.GetDecisionVectorDescriptionEntries()];
            expansion.Initialize(allFields.Select(v => v.Descriptor));
            mathExpansion.Initialize(allFields.Select(v => v.Descriptor));
            var vector = new DataVector([0, 0, 0], DateTime.UtcNow);
            ApplyFilterCycleAndAssertExpectedValues(expansion, mathExpansion, ref vector, 0, 11, 0);
            ApplyFilterCycleAndAssertExpectedValues(expansion, mathExpansion, ref vector, 1, 6, 0);
            ApplyFilterCycleAndAssertExpectedValues(expansion, mathExpansion, ref vector, 1, 1, 0);
            ApplyFilterCycleAndAssertExpectedValues(expansion, mathExpansion, ref vector, 1, 6, 0);
            ApplyFilterCycleAndAssertExpectedValues(expansion, mathExpansion, ref vector, 1, 7, 0);
            ApplyFilterCycleAndAssertExpectedValues(expansion, mathExpansion, ref vector, 1, 6, 0);
            ApplyFilterCycleAndAssertExpectedValues(expansion, mathExpansion, ref vector, 1, 6, 1);
            ApplyFilterCycleAndAssertExpectedValues(expansion, mathExpansion, ref vector, 1, 5, 1);
            ApplyFilterCycleAndAssertExpectedValues(expansion, mathExpansion, ref vector, 1, 6, 1);
            ApplyFilterCycleAndAssertExpectedValues(expansion, mathExpansion, ref vector, 1, 5, 1);
            ApplyFilterCycleAndAssertExpectedValues(expansion, mathExpansion, ref vector, 1, 4, 0);
            ApplyFilterCycleAndAssertExpectedValues(expansion, mathExpansion, ref vector, 1, 5, 0);
            ApplyFilterCycleAndAssertExpectedValues(expansion, mathExpansion, ref vector, 1, 6, 0);
            ApplyFilterCycleAndAssertExpectedValues(expansion, mathExpansion, ref vector, 1, 5, 0);
            ApplyFilterCycleAndAssertExpectedValues(expansion, mathExpansion, ref vector, 1, 7, 1);
        }

        static void ApplyFilterCycleAndAssertExpectedValues(FilterVectorExpansion expansion, MathVectorExpansion mathExpansion, ref DataVector vector, double seconds, double input, double expectedFilterValue)
        {
            vector = new(vector.Data, vector.Timestamp.AddSeconds(seconds));
            List<SensorSample> inputs = [new("MyName", input)];
            expansion.ApplyLegacyFilters(inputs);//for decision filters this does not do any change
            for (int i = 0; i < inputs.Count; i++)
                vector.Data[i] = inputs[i].Value;
            using (var ctx = mathExpansion.NewContext(vector))
                expansion.Apply(ctx);
            Assert.AreEqual(expectedFilterValue, vector.Data[1]);
        }
        private static Func<IEnumerable<IOconfFilter>> GetFiltersFunc(string filter) => () => [new(filter, 2)];
        private static List<VectorDescriptionItem> ToDescItemInputs(IEnumerable<string> inputs) =>
            inputs.Select(n => new VectorDescriptionItem("double", n, DataTypeEnum.Input)).ToList();
    }
}
