using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CA_DataUploaderLib.Extensions;

namespace UnitTests
{
    [TestClass]
    public class LinqExtensionsTests
    {
        [TestMethod]
        public void TriangleFilter1()
        {
            var now = DateTime.Now;
            List<Tuple<double, DateTime>> values = new List<Tuple<double, DateTime>>();
            values.Add(new Tuple<double, DateTime>(3, now));
            values.Add(new Tuple<double, DateTime>(3, now.AddSeconds(0.1)));
            values.Add(new Tuple<double, DateTime>(3, now.AddSeconds(0.2)));
            values.Add(new Tuple<double, DateTime>(3, now.AddSeconds(0.3)));
            values.Add(new Tuple<double, DateTime>(3, now.AddSeconds(0.4)));
            values.Add(new Tuple<double, DateTime>(3, now.AddSeconds(0.5)));
            values.Add(new Tuple<double, DateTime>(3, now.AddSeconds(0.6)));
            values.Add(new Tuple<double, DateTime>(3, now.AddSeconds(0.7)));
            values.Add(new Tuple<double, DateTime>(3, now.AddSeconds(0.8)));
            values.Add(new Tuple<double, DateTime>(3, now.AddSeconds(0.9)));
            var result = values.TriangleFilter(1);
            Assert.IsTrue(Math.Abs(3d - result) < 0.0000000001);
        }

        [TestMethod]
        public void TriangleFilter2()
        {
            var now = DateTime.Now;
            List<Tuple<double, DateTime>> values = new List<Tuple<double, DateTime>>();
            values.Add(new Tuple<double, DateTime>(3, now));
            values.Add(new Tuple<double, DateTime>(3, now.AddSeconds(0.1)));
            values.Add(new Tuple<double, DateTime>(3, now.AddSeconds(0.2)));
            values.Add(new Tuple<double, DateTime>(3, now.AddSeconds(0.3)));
            values.Add(new Tuple<double, DateTime>(3, now.AddSeconds(0.4)));
            values.Add(new Tuple<double, DateTime>(3, now.AddSeconds(0.5)));
            values.Add(new Tuple<double, DateTime>(3, now.AddSeconds(0.6)));
            values.Add(new Tuple<double, DateTime>(3, now.AddSeconds(0.7)));
            values.Add(new Tuple<double, DateTime>(3, now.AddSeconds(0.8)));
            values.Add(new Tuple<double, DateTime>(4, now.AddSeconds(0.9)));
            values.Add(new Tuple<double, DateTime>(4, now.AddSeconds(1.0)));
            values.Add(new Tuple<double, DateTime>(4, now.AddSeconds(1.1)));
            values.Add(new Tuple<double, DateTime>(4, now.AddSeconds(1.2)));
            values.Add(new Tuple<double, DateTime>(4, now.AddSeconds(1.3)));
            var result = values.TriangleFilter(1);
            Assert.IsTrue(Math.Abs(3.7272727272d - result) < 0.0000001);

        }
    }
}
