using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CA_DataUploaderLib;
using CA_DataUploaderLib.IOconf;
using System.Threading;
using CA_DataUploaderLib.Helpers;
using System.Linq;

namespace UnitTests
{
    [TestClass]
    public class TriangelFilterTests
    {
        public static LegacyFilterSample NewLegacyFilter(string filter) => IOconfFilter.LegacyFilters([new IOconfFilter(filter, 0)]).First();
        [TestMethod]
        public void TriangleFilter1()
        {
            var filter = NewLegacyFilter("Filter;MyFilter;Triangle;1;X");
            var now = DateTime.Now;
            filter.Input([new("X", 3) { TimeStamp = now }]);
            filter.Input([new("X", 3) { TimeStamp = now.AddSeconds(0.1) }]);
            filter.Input([new("X", 3) { TimeStamp = now.AddSeconds(0.2) }]);
            filter.Input([new("X", 3) { TimeStamp = now.AddSeconds(0.3) }]);
            filter.Input([new("X", 3) { TimeStamp = now.AddSeconds(0.4) }]);
            filter.Input([new("X", 3) { TimeStamp = now.AddSeconds(0.5) }]);
            filter.Input([new("X", 3) { TimeStamp = now.AddSeconds(0.6) }]);
            filter.Input([new("X", 3) { TimeStamp = now.AddSeconds(0.7) }]);
            filter.Input([new("X", 3) { TimeStamp = now.AddSeconds(0.8) }]);
            filter.Input([new("X", 3) { TimeStamp = now.AddSeconds(0.9) }]);
            var result = filter.Output.Value;
            Assert.IsTrue(Math.Abs(3d - result) < 0.0000000001, $"{result}");
        }

        [TestMethod]
        public void TriangleFilter2()
        {
            var filter = NewLegacyFilter("Filter;MyFilter;Triangle;1;X");
            var now = DateTime.Now;
            filter.Input([new("X", 3) { TimeStamp = now }]);
            filter.Input([new("X", 3) { TimeStamp = now.AddSeconds(0.1) }]);
            filter.Input([new("X", 3) { TimeStamp = now.AddSeconds(0.2) }]);
            filter.Input([new("X", 3) { TimeStamp = now.AddSeconds(0.3) }]);
            filter.Input([new("X", 3) { TimeStamp = now.AddSeconds(0.4) }]); 
            filter.Input([new("X", 3) { TimeStamp = now.AddSeconds(0.5) }]);
            filter.Input([new("X", 3) { TimeStamp = now.AddSeconds(0.6) }]);
            filter.Input([new("X", 3) { TimeStamp = now.AddSeconds(0.7) }]);
            filter.Input([new("X", 3) { TimeStamp = now.AddSeconds(0.8) }]);
            filter.Input([new("X", 4) { TimeStamp = now.AddSeconds(0.9) }]);
            filter.Input([new("X", 4) { TimeStamp = now.AddSeconds(1.0) }]);
            filter.Input([new("X", 4) { TimeStamp = now.AddSeconds(1.1) }]);
            filter.Input([new("X", 4) { TimeStamp = now.AddSeconds(1.2) }]);
            filter.Input([new("X", 4) { TimeStamp = now.AddSeconds(1.3) }]);
            var result = filter.Output.Value;
            Assert.IsTrue(Math.Abs(3.727272727d - result) < 0.0000001, $"{result} - {now:o}");
        }

        [TestMethod]
        public void TriangleFilterHandlesTimeAverageWithoutPrecisionLoss()
        { // this test is a stable reproduction of an issue reproduced by test 2 that only happens with some dates, resulting in a wrong value in the original test
            var filter = NewLegacyFilter("Filter;MyFilter;Triangle;1;X");
            var now = DateTime.ParseExact("2020-12-04T18:34:53.2064482+01:00", "o", Thread.CurrentThread.CurrentCulture);
            filter.Input([new("X", 3) { TimeStamp = now }]);
            filter.Input([new("X", 3) { TimeStamp = now.AddSeconds(0.1) }]);
            filter.Input([new("X", 3) { TimeStamp = now.AddSeconds(0.2) }]);
            filter.Input([new("X", 3) { TimeStamp = now.AddSeconds(0.3) }]);
            filter.Input([new("X", 3) { TimeStamp = now.AddSeconds(0.4) }]);
            filter.Input([new("X", 3) { TimeStamp = now.AddSeconds(0.5) }]);
            filter.Input([new("X", 3) { TimeStamp = now.AddSeconds(0.6) }]);
            filter.Input([new("X", 3) { TimeStamp = now.AddSeconds(0.7) }]);
            filter.Input([new("X", 3) { TimeStamp = now.AddSeconds(0.8) }]);
            filter.Input([new("X", 4) { TimeStamp = now.AddSeconds(0.9) }]);
            filter.Input([new("X", 4) { TimeStamp = now.AddSeconds(1.0) }]);
            filter.Input([new("X", 4) { TimeStamp = now.AddSeconds(1.1) }]);
            filter.Input([new("X", 4) { TimeStamp = now.AddSeconds(1.2) }]);
            filter.Input([new("X", 4) { TimeStamp = now.AddSeconds(1.3) }]);
            var result = filter.Output.Value;
            Assert.IsTrue(Math.Abs(3.727272727d - result) < 0.0000001, $"{result}");
        }

        [TestMethod]
        public void TriangleFilterHandlesManySamples()
        { // this test is to detect issues with averaging dates when having many samples
            var filter = NewLegacyFilter("Filter;MyFilter;Triangle;1;X");
            var now = DateTime.Now;
            filter.Input([new SensorSample("X", 3) { TimeStamp = now }]);
            filter.Input([new("X", 3) { TimeStamp = now.AddSeconds(0.1) }]);
            filter.Input([new("X", 3) { TimeStamp = now.AddSeconds(0.2) }]);
            filter.Input([new("X", 3) { TimeStamp = now.AddSeconds(0.3) }]);
            filter.Input([new("X", 3) { TimeStamp = now.AddSeconds(0.4) }]);
            filter.Input([new("X", 3) { TimeStamp = now.AddSeconds(0.5) }]);
            filter.Input([new("X", 3) { TimeStamp = now.AddSeconds(0.6) }]);
            filter.Input([new("X", 3) { TimeStamp = now.AddSeconds(0.7) }]);
            filter.Input([new("X", 3) { TimeStamp = now.AddSeconds(0.8) }]);
            filter.Input([new("X", 4) { TimeStamp = now.AddSeconds(0.9) }]);
            filter.Input([
                new("X", 4) { TimeStamp = now.AddSeconds(1.0) },
                new("X", 4) { TimeStamp = now.AddSeconds(1.0) },
                new("X", 4) { TimeStamp = now.AddSeconds(1.0) },
                new("X", 4) { TimeStamp = now.AddSeconds(1.0) },
                new("X", 4) { TimeStamp = now.AddSeconds(1.0) },
                new("X", 4) { TimeStamp = now.AddSeconds(1.0) },
                new("X", 4) { TimeStamp = now.AddSeconds(1.0) },
                new("X", 4) { TimeStamp = now.AddSeconds(1.0) },
                new("X", 4) { TimeStamp = now.AddSeconds(1.0) },
                new("X", 4) { TimeStamp = now.AddSeconds(1.0) },
                new("X", 4) { TimeStamp = now.AddSeconds(1.0) },
                new("X", 4) { TimeStamp = now.AddSeconds(1.0) },
                new("X", 4) { TimeStamp = now.AddSeconds(1.0) },
                new("X", 4) { TimeStamp = now.AddSeconds(1.0) },
                new("X", 4) { TimeStamp = now.AddSeconds(1.0) },
                new("X", 4) { TimeStamp = now.AddSeconds(1.0) },
                new("X", 4) { TimeStamp = now.AddSeconds(1.0) },
                new("X", 4) { TimeStamp = now.AddSeconds(1.0) },
                new("X", 4) { TimeStamp = now.AddSeconds(1.0) },
                new("X", 4) { TimeStamp = now.AddSeconds(1.0) },
                new("X", 4) { TimeStamp = now.AddSeconds(1.0) },
                new("X", 4) { TimeStamp = now.AddSeconds(1.0) },
                new("X", 4) { TimeStamp = now.AddSeconds(1.0) },
                new("X", 4) { TimeStamp = now.AddSeconds(1.0) },
                new("X", 4) { TimeStamp = now.AddSeconds(1.0) },
                new("X", 4) { TimeStamp = now.AddSeconds(1.0) },
                new("X", 4) { TimeStamp = now.AddSeconds(1.0) },
                new("X", 4) { TimeStamp = now.AddSeconds(1.0) },
                new("X", 4) { TimeStamp = now.AddSeconds(1.0) },
                new("X", 4) { TimeStamp = now.AddSeconds(1.0) },
                new("X", 4) { TimeStamp = now.AddSeconds(1.0) },
                new("X", 4) { TimeStamp = now.AddSeconds(1.0) },
                new("X", 4) { TimeStamp = now.AddSeconds(1.0) },
            ]);
            filter.Input([new("X", 4) { TimeStamp = now.AddSeconds(1.1) }]);
            filter.Input([new("X", 4) { TimeStamp = now.AddSeconds(1.2) }]);
            filter.Input([new("X", 4) { TimeStamp = now.AddSeconds(1.3) }]);
            var result = filter.Output.Value;
            Assert.IsTrue(Math.Abs(3.727272727d - result) < 0.0000001, $"{result}");

        }
    }
}
