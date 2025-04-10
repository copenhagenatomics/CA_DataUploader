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
            var (cmd, desc, conf) = NewConf("Math;MyMath;MyName + 2", "Math;MyName;0");
            Assert.AreEqual("MyMath,MyName", string.Join(',', desc._items.Select(e => e.Descriptor)));
        }

        [TestMethod]
        public void ExpandsVector()
        {
            var (cmd, desc, _) = NewConf("Math;MyName;0.2", "Math;MyMath;MyName + 2");
            var vector = new DataVector(new double[desc.Length], DateTime.UtcNow);
            cmd.MakeDecision([], DateTime.UtcNow.AddMilliseconds(100), ref vector, []);
            CollectionAssert.AreEqual(new[] {0.2,2.2}, vector.Data);
        }

        [TestMethod]
        public void MathDependingOnMathUsesLatestValue()
        {
            var (cmd, desc, _) = NewConf("Math;Math1;Math1 + 1", "Math;Math2;Math1");
            var vector = new DataVector(new double[desc.Length], DateTime.UtcNow);
            vector.Data[0] = 2d; vector.Data[1] = 2;
            cmd.MakeDecision([], DateTime.UtcNow.AddMilliseconds(100), ref vector, []);
            CollectionAssert.AreEqual(new[] { 3d, 3 }, vector.Data);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException), "If the Math-expression references a source which is not in the vector, this should cause an exception.")]
        public void MathReferencingSourceNotInVectorCausesException()
        {
            _ = NewConf("Math;MyMath;NotInVector + 2");
        }

        [TestMethod]
        public async Task CanExpandVectorInParallel()
        {
            var (cmd, desc, _) = NewConf(
                "Math;f1;f1",
                "Math;f2;f2",
                "Math;f3;f3",
                "Math;f4;f4",
                "Math;f5;f5",
                "Math;f6;f6",
                "Math;f7;f7",
                "Math;f8;f8",
                "Math;f9;f9",
                "Math;f10;f10",
                "Math;MyMath;f1 + f2 + f3 + f4 + f5 + f6 + f7 + f8 + f9 + f10"
                );
            await ParallelHelper.ForRacingThreads(0, 100, i =>
            {
                var values = new double[11];
                for (int j = 0; j < 10; j++)
                    values[j] = i + j;
                var expected = values.Take(10).Sum();
                return () =>
                {
                    var vector = new DataVector(values, DateTime.UtcNow);
                    cmd.MakeDecision([], DateTime.UtcNow, ref vector, []);
                    Assert.AreEqual(expected, vector.Data[10], "detected thread safety issue");
                };
            });
        }

        public static MathVectorExpansion GetInitializedMathExpansion(IOconfMath math, IEnumerable<string> allvectorfields)
        {
            var expansion = GetNewMathExpansion(math);
            expansion.Initialize(allvectorfields);
            return expansion;
        }
        public static MathVectorExpansion GetNewMathExpansion(IOconfMath math) => new([math]);
        private static (CommandHandler cmd, VectorDescription desc, IOconfFile conf) NewConf(params string[] lines)
        {
            var conf = new IOconfFile([.. lines]);
            var cmd = new CommandHandler(conf, runCommandLoop: false);
            var desc = cmd.GetFullSystemVectorDescription();
            return (cmd, desc, conf);
        }
    }
}
