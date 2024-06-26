using CA_DataUploaderLib;
using CA_DataUploaderLib.IOconf;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

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
            math.Apply(new DataVector(values, DateTime.UtcNow));
            CollectionAssert.AreEqual(new[] { 0.2, 2.2 }, values);
        }

        [TestMethod]
        public void MathDependingOnMathUsesLatestValue()
        {
            var math = new MathVectorExpansion(() => new[] { new IOconfMath("Math;Math1;Math1 + 1", 0), new("Math;Math2;Math1", 0) });
            math.Initialize(new[] { "Math1", "Math2" });
            var values = new[] { 2d, 2 };
            math.Apply(new DataVector(values, DateTime.UtcNow));
            CollectionAssert.AreEqual(new[] { 3d, 3 }, values);
        }

        [TestMethod]
        public void IgnoresUnusedValues()
        {
            var math = GetInitializedMathExpansion(new("Math;MyMath;MyName + 2", 2), new[] { "MyName", "UnusedValue", "MyMath" });
            var values = new[]{0.2, 3, 0};
            math.Apply(new DataVector(values, DateTime.UtcNow));
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
            math.Apply(new DataVector(values, DateTime.UtcNow));

            CollectionAssert.AreEqual(new[] { 0.2, 2.2, 100 }, values, "the extra math statement must be ignored and must not incorrectly update the output");
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException), "If the Math-expression references a source which is not in the vector, this should cause an exception.")]
        public void MathReferencingSourceNotInVectorCausesException()
        {
            GetInitializedMathExpansion(new("Math;MyMath;NotInVector + 2", 2), new[] { "MyMath" });
        }

        [TestMethod]
        public async Task CanExpandVectorInParallel()
        {
            var math = GetInitializedMathExpansion(
                new("Math;MyMath;f1 + f2 + f3 + f4 + f5 + f6 + f7 + f8 + f9 + f10", 0), 
                new[] { "f1", "f2", "f3", "f4", "f5", "f6", "f7", "f8", "f9", "f10", "MyMath" });
            await ParallelHelper.ForRacingThreads(0, 100, i =>
            {
                var values = new double[11];
                for (int j = 0; j < 10; j++)
                    values[j] = i + j;
                var expected = values.Take(10).Sum();
                return () =>
                {
                    math.Apply(new DataVector(values, DateTime.UtcNow));
                    Assert.AreEqual(expected, values[10], "detected thread safety issue");
                };
            });
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
