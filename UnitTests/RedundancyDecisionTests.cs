#nullable enable
using System.Collections.Generic;
using CA.LoopControlPluginBase;
using CA_DataUploaderLib;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace UnitTests
{
    [TestClass]
    public class RedundancyDecisionTests
    {
        DecisionTestContext testContext = new(new());
        private ref double Field(string field) => ref testContext.Field(field);
        private void MakeDecisions(double secondsSinceStart = 0) => testContext.MakeDecisions(secondsSinceStart: secondsSinceStart);

        [TestInitialize]
        public void Setup() => testContext = new DecisionTestContext(
            new List<LoopControlDecision>() {
                new Redundancy.Decision(new (
                    "red5",
                    new(){"a","b","c","d", "e" },
                    new(){ new() { "abox"}, new() { "bbox"}, new() { "cbox"}, new() { "dbox"}, new() { "ebox"} },
                    (0,2000),
                    10000)),
                new Redundancy.Decision(new (
                    "red4",
                    new(){"a","b","c","d" },
                    new(){ new() { "abox"}, new() { "bbox"}, new() { "cbox"}, new() { "dbox"} },
                    (0,2000), 
                    -0.001))},
            new()
            {
                { "abox", (int)BaseSensorBox.ConnectionState.ReceivingValues },
                { "bbox", (int)BaseSensorBox.ConnectionState.ReceivingValues },
                { "cbox", (int)BaseSensorBox.ConnectionState.ReceivingValues },
                { "dbox", (int)BaseSensorBox.ConnectionState.ReceivingValues },
                { "ebox", (int)BaseSensorBox.ConnectionState.ReceivingValues },
                { "a", 10 },
                { "b", 14 },
                { "c", 17 },
                { "d", 19 },
                { "e", 20 }
            });

        [TestMethod]
        public void UsesMedianOf5()
        {
            MakeDecisions();
            Assert.AreEqual(17.0, Field("red5"));
            Field("c") = 21;
            MakeDecisions();
            Assert.AreEqual(19.0, Field("red5"));
            Field("c") = 13;
            MakeDecisions();
            Assert.AreEqual(14.0, Field("red5"));
            Field("c") = 17;
            Field("cbox") = (int)BaseSensorBox.ConnectionState.NoDataAvailable;
            MakeDecisions();
            Assert.AreEqual(16.5, Field("red5"), "must ignore stale sensor data and use median of the remaining 4");
            Field("cbox") = (int)BaseSensorBox.ConnectionState.Connected;
            Field("c") = 3000;
            MakeDecisions();
            Assert.AreEqual(16.5, Field("red5"), "must ignore stale sensor data outside the range and use median of the remaining 4");
            Field("abox") = Field("bbox") = Field("cbox") = Field("dbox") = Field("ebox") = (int)BaseSensorBox.ConnectionState.NoDataAvailable;
            MakeDecisions();
            Assert.AreEqual(10000, Field("red5"), "must use default invalid value when all sensors are stale");
        }

        [TestMethod]
        public void UsesMedianOf4()
        {
            MakeDecisions();
            Assert.AreEqual(15.5, Field("red4"));
            Field("c") = 21;
            MakeDecisions();
            Assert.AreEqual(16.5, Field("red4"));
            Field("c") = 13;
            MakeDecisions();
            Assert.AreEqual(13.5, Field("red4"));
            Field("c") = 17;
            Field("cbox") = (int)BaseSensorBox.ConnectionState.NoDataAvailable;
            MakeDecisions();
            Assert.AreEqual(14, Field("red4"), "must ignore stale sensor data and use median of the remaining 3");
            Field("cbox") = (int)BaseSensorBox.ConnectionState.Connected;
            Field("c") = 3000;
            MakeDecisions();
            Assert.AreEqual(14, Field("red4"), "must ignore stale sensor data outside the range and use median of the remaining 3");
            Field("abox") = Field("bbox") = Field("cbox") = Field("dbox") = (int)BaseSensorBox.ConnectionState.NoDataAvailable;
            MakeDecisions();
            Assert.AreEqual(-0.001, Field("red4"), "must use default invalid value when all sensors are stale");
        }
    }
}