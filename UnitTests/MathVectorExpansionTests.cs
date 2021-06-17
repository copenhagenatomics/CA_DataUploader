using CA_DataUploaderLib;
using CA_DataUploaderLib.IOconf;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace UnitTests
{
    [TestClass]
    public class MathVectorExpansionTests
    {
        [TestMethod]
        public void VectorDescriptionIncludesMath()
        {
            static IEnumerable<IOconfMath> GetTestMath() => new[] { new IOconfMath("Math;MyMath;MyName + 2", 2) };
            var math = new MathVectorExpansion(GetTestMath);
            Assert.AreEqual("MyMath", string.Join(Environment.NewLine, math.GetVectorDescriptionEntries().Select(e => e.Descriptor)));
        }

        [TestMethod]
        public void ExpandsVector()
        {
            static IEnumerable<IOconfMath> GetTestMath() => new[] { new IOconfMath("Math;MyMath;MyName + 2", 2) };
            var math = new MathVectorExpansion(GetTestMath);
            var values = new List<SensorSample>() { new SensorSample(new IOconfInput("KType;MyName", 1, "KType", false, false, null), 0.2) };
            math.Expand(values);
            CollectionAssert.AreEqual(new[] { 0.2, 2.2 }, values.Select(v => v.Value).ToArray());
        }

        [TestMethod]
        public void IgnoresUnusedValues()
        {
            static IEnumerable<IOconfMath> GetTestMath() => new[] { new IOconfMath("Math;MyMath;MyName + 2", 2) };
            var math = new MathVectorExpansion(GetTestMath);
            var values = new List<SensorSample>() {
                new SensorSample(new IOconfInput("KType;MyName", 1, "KType", false, false, null), 0.2),
                new SensorSample(new IOconfInput("KType;UnusedValue", 1, "KType", false, false, null), 3)
            };
            math.Expand(values);
            CollectionAssert.AreEqual(new[] { 0.2, 3, 2.2 }, values.Select(v => v.Value).ToArray());
        }

        [TestMethod]
        public void IgnoresIOConfMathChanges()
        {
            var ioconfEntries = new[] { new IOconfMath("Math;MyMath;MyName + 2", 2) };
            IEnumerable<IOconfMath> GetTestMath() => ioconfEntries;
            var math = new MathVectorExpansion(GetTestMath);
            var values = new List<SensorSample>() { new SensorSample(new IOconfInput("KType;MyName", 1, "KType", false, false, null), 0.2) };

            ioconfEntries = new[] { new IOconfMath("Math;MyMath;MyName + 2", 2), new IOconfMath("Math;MyMath2;MyName + 3", 2) };
            math.Expand(values);

            CollectionAssert.AreEqual(
                new[] { 0.2, 2.2 }, 
                values.Select(v => v.Value).ToArray(), 
                "only the single math statement sent to the constructor must be used");
        }
    }
}
