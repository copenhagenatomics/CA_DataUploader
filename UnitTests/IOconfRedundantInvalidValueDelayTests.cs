using CA.LoopControlPluginBase;
using CA_DataUploaderLib;
using CA_DataUploaderLib.IOconf;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace UnitTests
{
    [TestClass]
    public class IOconfRedundantInvalidValueDelayTests
    {
        [TestMethod]
        [DataRow("", 0d, 0d, 0d)]
        [DataRow("RedundantInvalidValueDelay; 5", 5d, 5d, 5d)]
        [DataRow("RedundantInvalidValueDelay; GroupA; 15", 15d, 0d, 0d)]
        [DataRow("RedundantInvalidValueDelay; GroupA; 15\nRedundantInvalidValueDelay; GroupB; 2", 15d, 2d, 0d)]
        [DataRow("RedundantInvalidValueDelay; 5\nRedundantInvalidValueDelay; GroupA; 15\nRedundantInvalidValueDelay; GroupB; 2", 15d, 2d, 5d)]
        [DataRow("RedundantInvalidValueDelay; 5\nRedundantInvalidValueDelay; GroupA; 0", 0d, 5d, 5d)]
        [DataRow("RedundantInvalidValueDelay; 0.5\nRedundantInvalidValueDelay; GroupA; 1.25", 1.25d, 0.5d, 0.5d)]
        [DataRow("RedundantInvalidValueDelay; 0.5\nRedundantInvalidValueDelay; GroupB; 1.25", 0.5d, 1.25d, 0.5d)]
        [DataRow("RedundantInvalidValueDelay; 0.5\nRedundantInvalidValueDelay; GroupC; 1.25", 0.5d, 0.5d, 1.25d)]
        public void ToDecisionConfigs_ResolvesDelaysIndependently(
            string delayRows, double groupA, double groupB, double groupC)
        {
            foreach (var reverseRows in new[] { false, true })
            {
                var configs = CreateConfigs(delayRows, reverseRows);

                Assert.HasCount(3, configs);
                Assert.AreEqual(groupA, configs["GroupA"].InvalidValueDelay);
                Assert.AreEqual(groupB, configs["GroupB"].InvalidValueDelay);
                Assert.AreEqual(groupC, configs["GroupC"].InvalidValueDelay);
            }
        }

        [TestMethod]
        [DataRow("RedundantInvalidValueDelay; 5\nRedundantInvalidValueDelay; 10")]
        [DataRow("RedundantInvalidValueDelay; GroupA; 5\nRedundantInvalidValueDelay; GroupA; 10")]
        [DataRow("RedundantInvalidValueDelay; GroupA; 5\nRedundantInvalidValueDelay; groupa; 10")]
        public void DuplicateDelays_AreRejected(string delayRows)
        {
            var exception = Assert.Throws<FormatException>(() => CreateConfigs(delayRows));

            Assert.Contains("Duplicate configuration key", exception.Message);
        }

        [TestMethod]
        [DataRow("RedundantInvalidValueDelay")]
        [DataRow("RedundantInvalidValueDelay; not_a_delay")]
        [DataRow("RedundantInvalidValueDelay; GroupA")]
        [DataRow("RedundantInvalidValueDelay; GroupA; not_a_delay")]
        [DataRow("RedundantInvalidValueDelay; ; 5")]
        [DataRow("RedundantInvalidValueDelay; 1InvalidGroup; 5")]
        [DataRow("RedundantInvalidValueDelay; GroupA; 5; extra")]
        public void MalformedDelays_AreRejected(string delayRow)
        {
            Assert.Throws<FormatException>(() => CreateConfigs(delayRow));
        }

        [TestMethod]
        public void DelayForUnknownGroup_IsRejected()
        {
            var exception = Assert.Throws<FormatException>(() =>
                CreateConfigs("RedundantInvalidValueDelay; UnknownGroup; 5"));

            Assert.Contains("missing sensors", exception.Message);
            Assert.Contains("UnknownGroup", exception.Message);
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void BoardOutages_UseIndependentDelaysAndResetAfterRecovery(bool recoverFromShortOutageFirst)
        {
            var configs = CreateConfigs("""
                RedundantInvalidValueDelay; 5
                RedundantInvalidValueDelay; GroupA; 15
                RedundantInvalidValueDelay; GroupB; 0
                """);

            var boardStates = configs.Values
                .SelectMany(c => c.SensorBoardStates)
                .SelectMany(states => states)
                .Distinct()
                .ToList();

            Assert.IsNotEmpty(boardStates);

            var initialValues = boardStates.ToDictionary(
                name => name,
                _ => (double)BaseSensorBox.ConnectionState.ReceivingValues);
            initialValues.Add("temperature", 42d);

            var context = new DecisionTestContext(
                [.. configs.Values.Select(c => (LoopControlDecision)new Redundancy.Decision(c))],
                initialValues);

            AssertValues(42, 42, 42);

            SetConnected(false);
            context.MakeDecisions(secondsSinceStart: 1);
            AssertValues(42, 10000, 42);

            double outageStart = 1;
            if (recoverFromShortOutageFirst)
            {
                SetConnected(true);
                context.MakeDecisions(secondsSinceStart: 2);
                AssertValues(42, 42, 42);

                SetConnected(false);
                outageStart = 3;
                context.MakeDecisions(secondsSinceStart: outageStart);
                AssertValues(42, 10000, 42);
            }

            context.MakeDecisions(secondsSinceStart: outageStart + 4.999);
            AssertValues(42, 10000, 42);

            context.MakeDecisions(secondsSinceStart: outageStart + 5);
            AssertValues(42, 10000, 10000);

            context.MakeDecisions(secondsSinceStart: outageStart + 14.999);
            AssertValues(42, 10000, 10000);

            context.MakeDecisions(secondsSinceStart: outageStart + 15);
            AssertValues(10000, 10000, 10000);

            SetConnected(true);
            context.MakeDecisions(secondsSinceStart: outageStart + 16);
            AssertValues(42, 42, 42);

            void SetConnected(bool connected)
            {
                foreach (var state in boardStates)
                    context.Field(state) = connected
                        ? (int)BaseSensorBox.ConnectionState.ReceivingValues
                        : (int)BaseSensorBox.ConnectionState.NoDataAvailable;
            }

            void AssertValues(double groupA, double groupB, double groupC)
            {
                Assert.AreEqual(groupA, context.Field("GroupA"), "GroupA");
                Assert.AreEqual(groupB, context.Field("GroupB"), "GroupB");
                Assert.AreEqual(groupC, context.Field("GroupC"), "GroupC");
            }
        }

        private static Dictionary<string, Redundancy.Decision.Config> CreateConfigs(string delayRows, bool reverseRows = false)
        {
            var loader = new IOconfLoader();
            Redundancy.RegisterSystemExtensions(loader);

            var rows = new List<string>
            {
                "Map; 4900553433511235353734; tm01",
                "TypeJ; temperature; tm01; 1",
                "RedundantSensors; GroupA; temperature",
                "RedundantSensors; GroupB; temperature",
                "RedundantSensors; GroupC; temperature"
            };
            rows.AddRange(delayRows.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

            if (reverseRows)
                rows.Reverse();

            var ioconf = new IOconfFile(loader, rows);
            return Redundancy.IOconfRedundant.ToDecisionConfigs(
                ioconf.GetEntries<Redundancy.IOconfRedundant>(), ioconf)
                .ToDictionary(c => c.Name);
        }
    }
}