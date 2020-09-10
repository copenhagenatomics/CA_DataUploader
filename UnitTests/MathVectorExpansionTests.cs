using CA_DataUploaderLib;
using CA_DataUploaderLib.IOconf;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;

namespace UnitTests
{
    [TestClass]
    public class MathVectorExpansionTests
    {
        [TestMethod]
        public void VectorDescriptionIncludesMath()
        {
            IEnumerable<IOconfMath> GetTestMath() => new[] { new IOconfMath("Math;MyMath;MyName + 2", 2) };
            var items = new List<VectorDescriptionItem> { new VectorDescriptionItem("MyType", "MyName", DataTypeEnum.Input) };
            var math = new MathVectorExpansion(new VectorDescription(items, "my hardware", "my software"), GetTestMath);
            Assert.AreEqual("MyName" + Environment.NewLine + "MyMath", math.VectorDescription.GetVectorItemDescriptions());
        }

        [TestMethod]
        public void ExpandsVector()
        {
            IEnumerable<IOconfMath> GetTestMath() => new[] { new IOconfMath("Math;MyMath;MyName + 2", 2) };
            var items = new List<VectorDescriptionItem> { new VectorDescriptionItem("MyType", "MyName", DataTypeEnum.Input) };
            var math = new MathVectorExpansion(new VectorDescription(items, "my hardware", "my software"), GetTestMath);
            var values = new List<double> { 0.2 };
            math.Expand(values);
            CollectionAssert.AreEqual(new[] { 0.2, 2.2 }, values);
        }

        [TestMethod]
        public void RejectsUnexpectedVectorLength()
        {
            IEnumerable<IOconfMath> GetTestMath() => new[] { new IOconfMath("Math;MyMath;MyName + 2", 2) };
            var items = new List<VectorDescriptionItem> { new VectorDescriptionItem("MyType", "MyName", DataTypeEnum.Input) };
            var math = new MathVectorExpansion(new VectorDescription(items, "my hardware", "my software"), GetTestMath);
            var values = new List<double> { 0.2, 3 };
            var ex = Assert.ThrowsException<ArgumentException>(() => math.Expand(values));
            Assert.AreEqual("wrong vector length (input, expected): 2 <> 1", ex.Message);
        }

        [TestMethod]
        public void IgnoresIOConfMathChanges()
        {
            var ioconfEntries = new[] { new IOconfMath("Math;MyMath;MyName + 2", 2) };
            IEnumerable<IOconfMath> GetTestMath() => ioconfEntries;
            var items = new List<VectorDescriptionItem> { new VectorDescriptionItem("MyType", "MyName", DataTypeEnum.Input) };
            var math = new MathVectorExpansion(new VectorDescription(items, "my hardware", "my software"), GetTestMath);
            var values = new List<double> { 0.2 };

            ioconfEntries = new[] { new IOconfMath("Math;MyMath;MyName + 2", 2), new IOconfMath("Math;MyMath2;MyName + 3", 2) };
            math.Expand(values);

            CollectionAssert.AreEqual(new[] { 0.2, 2.2 }, values, "only the single math statement sent to the constructor must be used");
        }
    }
}
