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
        static double[] TestFilterInputs => [11,6,1,6,7,6,6,5,6,5,4,5,6,5,7];
        public record TestRecord(int Length, string Expression, string ExpectedValues);
        public static IEnumerable<object[]> TestData =>
            [
                new [] {new TestRecord(3, "MyName - 2 >= 3", "0,0,0,0,0,0,1,1,1,1,0,0,0,0,1")},
                new [] {new TestRecord(3, "3 <= MyName - 2", "0,0,0,0,0,0,1,1,1,1,0,0,0,0,1")},
                new [] {new TestRecord(1, "MyName == 6", "0,0,0,0,0,0,1,0,0,0,0,0,0,0,0")},
                new [] {new TestRecord(1, "MyName != 6", "0,0,0,0,0,0,0,0,0,0,1,1,0,0,1")},
                new [] {new TestRecord(1, "if(MyName > 5, MyName == 7, true)", "0,0,0,0,0,0,0,0,0,0,1,1,0,0,1")},
            ];
        [TestMethod]
        public void InputsIncludeLegacyFilter()
        {
            var inputs = ToDescItemInputs(["MyName"]);
            var expansion = new FilterVectorExpansion(inputs, GetFilters("Filter;MyFilter;Triangle;3;MyName"), CALogLevel.Normal);
            Assert.AreEqual("MyName,MyFilter_filter", string.Join(',', inputs.Select(i => i.Descriptor)));
            Assert.AreEqual("", string.Join(',', expansion.GetDecisionVectorDescriptionEntries().Select(i => i.Descriptor)));
        }

        [TestMethod]
        public void VectorIncludesSustainedFilter()
        {
            var inputs = ToDescItemInputs(["MyName"]);
            var expansion = new FilterVectorExpansion(inputs, GetFilters("Filter;MyFilter;Sustained;3;MyName + 2 > 3"), CALogLevel.Normal);
            Assert.AreEqual("MyName", string.Join(',', inputs.Select(i => i.Descriptor)));
            Assert.AreEqual("MyFilter,MyFilter_targettime", string.Join(',', expansion.GetDecisionVectorDescriptionEntries().Select(i => i.Descriptor)));
        }

        [TestMethod]
        public void SustainedFilterRequiresBooleanExpression()
        {
            var ex = Assert.Throws<FormatException>(() => GetFilters("Filter;MyFilter;Sustained;3;MyName"));
            StringAssert.Contains(ex.Message, "Only boolean filter expressions are supported");
            StringAssert.Contains(ex.Message, "Filter;MyFilter;Sustained;3;MyName");
        }

        [TestMethod, DynamicData(nameof(TestData))]
        public void AppliesSustainedFilterParametrized(TestRecord record)
        {
            var inputsDesc = ToDescItemInputs(["MyName"]);
            var expansion = new FilterVectorExpansion(inputsDesc, GetFilters($"Filter;MyFilter;Sustained;{record.Length};{record.Expression}"), CALogLevel.Normal);
            var mathExpansion = new MathVectorExpansion([]);
            List<VectorDescriptionItem> allFields = [.. inputsDesc, .. expansion.GetDecisionVectorDescriptionEntries()];
            expansion.Initialize(allFields.Select(v => v.Descriptor));
            mathExpansion.Initialize(allFields.Select(v => v.Descriptor));
            var vector = new DataVector([0, 0, 0], DateTime.UtcNow);
            var filterValues = string.Join(',', TestFilterInputs.Select(i => ApplyFilterCycle(ref vector, i)));
            Assert.AreEqual(record.ExpectedValues, filterValues);

            double ApplyFilterCycle(ref DataVector vector, double input)
            {
                vector = new(vector.Data, vector.Timestamp.AddSeconds(1));
                List<SensorSample> inputs = [new("MyName", input)];
                expansion.ApplyLegacyFilters(inputs);//for decision filters this does not do any change
                for (int i = 0; i < inputs.Count; i++)
                    vector.Data[i] = inputs[i].Value;
                using (var ctx = mathExpansion.NewContext(vector))
                    expansion.Apply(ctx);
                return vector.Data[1];
            }
        }

        private static IEnumerable<IOconfFilter> GetFilters(string filter) => [new(filter, 2)];
        private static List<VectorDescriptionItem> ToDescItemInputs(IEnumerable<string> inputs) =>
            inputs.Select(n => new VectorDescriptionItem("double", n, DataTypeEnum.Input)).ToList();
    }
}
