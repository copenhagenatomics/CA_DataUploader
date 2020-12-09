﻿using CA_DataUploaderLib;
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
            var items = new List<VectorDescriptionItem> { new VectorDescriptionItem("MyType", "MyName", DataTypeEnum.Input) };
            var math = new MathVectorExpansion(new VectorDescription(items, "my hardware", "my software"), GetTestMath);
            Assert.AreEqual("MyName" + Environment.NewLine + "MyMath", math.VectorDescription.GetVectorItemDescriptions());
        }

        [TestMethod]
        public void ExpandsVector()
        {
            static IEnumerable<IOconfMath> GetTestMath() => new[] { new IOconfMath("Math;MyMath;MyName + 2", 2) };
            var items = new List<VectorDescriptionItem> { new VectorDescriptionItem("MyType", "MyName", DataTypeEnum.Input) };
            var math = new MathVectorExpansion(new VectorDescription(items, "my hardware", "my software"), GetTestMath);
            var values = new List<SensorSample>() { new SensorSample(new IOconfInput("KType;MyName", 1, "KType"), 0.2) };
            math.Expand(values);
            CollectionAssert.AreEqual(new[] { 0.2, 2.2 }, values.Select(v => v.Value).ToArray());
        }

        [TestMethod]
        public void RejectsUnexpectedVectorLength()
        {
            static IEnumerable<IOconfMath> GetTestMath() => new[] { new IOconfMath("Math;MyMath;MyName + 2", 2) };
            var items = new List<VectorDescriptionItem> { new VectorDescriptionItem("MyType", "MyName", DataTypeEnum.Input) };
            var math = new MathVectorExpansion(new VectorDescription(items, "my hardware", "my software"), GetTestMath);
            var values = new List<SensorSample>() {
                new SensorSample(new IOconfInput("KType;MyName", 1, "KType"), 0.2),
                new SensorSample(new IOconfInput("KType;UnexpectedValue", 1, "KType"), 3)
            };
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
            var values = new List<SensorSample>() { new SensorSample(new IOconfInput("KType;MyName", 1, "KType"), 0.2) };

            ioconfEntries = new[] { new IOconfMath("Math;MyMath;MyName + 2", 2), new IOconfMath("Math;MyMath2;MyName + 3", 2) };
            math.Expand(values);

            CollectionAssert.AreEqual(
                new[] { 0.2, 2.2 }, 
                values.Select(v => v.Value).ToArray(), 
                "only the single math statement sent to the constructor must be used");
        }
    }
}
