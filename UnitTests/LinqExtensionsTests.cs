using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CA_DataUploaderLib.Extensions;
using CA_DataUploaderLib;

namespace UnitTests
{
    [TestClass]
    public class LinqExtensionsTests
    {
        [TestMethod]
        public void TriangleFilter1()
        {
            var now = DateTime.Now;
            List<List<SensorSample>> values = new List<List<SensorSample>>();
            values.Add(new List<SensorSample>() { new SensorSample("X", 3) { TimeStamp = now } });
            values.Add(new List<SensorSample>() { new SensorSample("X", 3) { TimeStamp = now.AddSeconds(0.1) } });
            values.Add(new List<SensorSample>() { new SensorSample("X", 3) { TimeStamp = now.AddSeconds(0.2) } });
            values.Add(new List<SensorSample>() { new SensorSample("X", 3) { TimeStamp = now.AddSeconds(0.3) } });
            values.Add(new List<SensorSample>() { new SensorSample("X", 3) { TimeStamp = now.AddSeconds(0.4) } });
            values.Add(new List<SensorSample>() { new SensorSample("X", 3) { TimeStamp = now.AddSeconds(0.5) } });
            values.Add(new List<SensorSample>() { new SensorSample("X", 3) { TimeStamp = now.AddSeconds(0.6) } });
            values.Add(new List<SensorSample>() { new SensorSample("X", 3) { TimeStamp = now.AddSeconds(0.7) } });
            values.Add(new List<SensorSample>() { new SensorSample("X", 3) { TimeStamp = now.AddSeconds(0.8) } });
            values.Add(new List<SensorSample>() { new SensorSample("X", 3) { TimeStamp = now.AddSeconds(0.9) } });
            var result = values.TriangleFilter(1);
            Assert.IsTrue(Math.Abs(3d - result) < 0.0000000001);
        }

        [TestMethod]
        public void TriangleFilter2()
        {
            var now = DateTime.Now;
            List<List<SensorSample>> values = new List<List<SensorSample>>();
            values.Add(new List<SensorSample>() { new SensorSample("X", 3) { TimeStamp = now } });
            values.Add(new List<SensorSample>() { new SensorSample("X", 3) { TimeStamp = now.AddSeconds(0.1) } });
            values.Add(new List<SensorSample>() { new SensorSample("X", 3) { TimeStamp = now.AddSeconds(0.2) } });
            values.Add(new List<SensorSample>() { new SensorSample("X", 3) { TimeStamp = now.AddSeconds(0.3) } });
            values.Add(new List<SensorSample>() { new SensorSample("X", 3) { TimeStamp = now.AddSeconds(0.4) } });
            values.Add(new List<SensorSample>() { new SensorSample("X", 3) { TimeStamp = now.AddSeconds(0.5) } });
            values.Add(new List<SensorSample>() { new SensorSample("X", 3) { TimeStamp = now.AddSeconds(0.6) } });
            values.Add(new List<SensorSample>() { new SensorSample("X", 3) { TimeStamp = now.AddSeconds(0.7) } });
            values.Add(new List<SensorSample>() { new SensorSample("X", 3) { TimeStamp = now.AddSeconds(0.8) } });
            values.Add(new List<SensorSample>() { new SensorSample("X", 4) { TimeStamp = now.AddSeconds(0.9) } });
            values.Add(new List<SensorSample>() { new SensorSample("X", 4) { TimeStamp = now.AddSeconds(1.0) } });
            values.Add(new List<SensorSample>() { new SensorSample("X", 4) { TimeStamp = now.AddSeconds(1.1) } });
            values.Add(new List<SensorSample>() { new SensorSample("X", 4) { TimeStamp = now.AddSeconds(1.2) } });
            values.Add(new List<SensorSample>() { new SensorSample("X", 4) { TimeStamp = now.AddSeconds(1.3) } });
            var result = values.TriangleFilter(1);
            Assert.IsTrue(Math.Abs(3.7272746314d - result) < 0.0000001);

        }
    }
}
