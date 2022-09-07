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
            var math = GetNewMathExpansion(new("Math;MyMath;MyName + 2", 2));
            Assert.AreEqual("MyMath", string.Join(Environment.NewLine, math.GetVectorDescriptionEntries().Select(e => e.Descriptor)));
        }

        [TestMethod]
        public void ExpandsVector()
        {
            var math = GetInitializedMathExpansion(new("Math;MyMath;MyName + 2", 2), new[] { "MyName", "MyMath" });
            var values = new[]{0.2, 0};
            math.Apply(values);
            CollectionAssert.AreEqual(new[] { 0.2, 2.2 }, values);
        }

        [TestMethod]
        public void IgnoresUnusedValues()
        {
            var math = GetInitializedMathExpansion(new("Math;MyMath;MyName + 2", 2), new[] { "MyName", "UnusedValue", "MyMath" });
            var values = new[]{0.2, 3, 0};
            math.Apply(values);
            CollectionAssert.AreEqual(new[] { 0.2, 3, 2.2 }, values);
        }

        [TestMethod]
        public void IgnoresIOConfMathChanges()
        {
            var ioconfEntries = new IOconfMath[] { new("Math;MyMath;MyName + 2", 2) };
            IEnumerable<IOconfMath> GetTestMath() => ioconfEntries;
            var math = new MathVectorExpansion(GetTestMath);
            math.Initialize(new[] { "MyName", "MyMath", "MyOutput" });
            var values = new[]{0.2,0,100};

            ioconfEntries = new IOconfMath[] { new("Math;MyMath;MyName + 2", 2), new("Math;MyMath2;MyName + 3", 2) };
            math.Apply(values);

            CollectionAssert.AreEqual(new[] { 0.2, 2.2, 100 }, values, "the extra math statement must be ignored and must not incorrectly update the output");
        }

        public static MathVectorExpansion GetInitializedMathExpansion(IOconfMath math, IEnumerable<string> allvectorfields)
        {
            var expansion = GetNewMathExpansion(math);
            expansion.Initialize(allvectorfields);
            return expansion;
        }
        public static MathVectorExpansion GetNewMathExpansion(IOconfMath math) => new(() => new[] { math });
    }
}
