using CA_DataUploaderLib;
using CA_DataUploaderLib.IOconf;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Plugin = CA.LoopControlPluginBase;

namespace UnitTests
{
    [TestClass]
    [DoNotParallelize]
    public class AlertVectorTests
    {
        private ISimpleLogger originalLogger = null!;

        [TestInitialize]
        public void SaveLogger() => originalLogger = CALog.LoggerForUserOutput;

        [TestCleanup]
        public void RestoreLogger() => CALog.LoggerForUserOutput = originalLogger;

        [DataRow("", "alert")]
        [DataRow(";tags:level=alert", "alert")]
        [DataRow(";tags:level=error", "error")]
        [DataRow(";tags:level=info", "info")]
        [TestMethod]
        public void ChannelTracksConditionOnEveryCycle(string tags, string level)
        {
            var config = new IOconfFile([$"Alert;overPressure;pressure > 1.5;5{tags}"]);
            using var cmd = CreateHandler(config, "pressure");
            var field = cmd.GetFullSystemVectorDescription()._items.Single(i => i.Descriptor == $"overPressure_{level}");
            Assert.AreEqual(DataTypeEnum.State, field.DirectionType);
            Assert.IsTrue(field.Upload);
            DataVector? vector = null;
            var time = new DateTime(2026, 1, 1);
            foreach (var (pressure, expected) in new[] { (1.5, 0d), (2d, 1d), (2d, 1d), (1.5, 0d) })
            {
                cmd.MakeDecision([new("pressure", pressure)], time, ref vector, []);
                Assert.AreEqual(expected, vector[cmd.GetFullSystemVectorDescription()._items.IndexOf(field)]);
                Assert.IsNull(cmd.DequeueEvents());
                time = time.AddSeconds(1);
            }
        }

        [DataRow("", "alert", EventType.Alert)]
        [DataRow(";tags:level=alert", "alert", EventType.Alert)]
        [DataRow(";tags:level=error", "error", EventType.LogError)]
        [DataRow(";tags:level=info", "info", EventType.Log)]
        [TestMethod]
        public async Task ActivationEmitsOnceAndRateLimitsEventsAndCommands(string tags, string level, EventType eventType)
        {
            var config = new IOconfFile([$"Alert;overPressure;pressure > 1.5;5;hej{tags}"]);
            using var cmd = CreateHandler(config, "pressure");
            CALog.LoggerForUserOutput = new CALog.EventsLogger(config, cmd);
            _ = new Alerts(config, cmd);
            var executions = 0;
            cmd.AddCommand("hej", _ => { executions++; return true; });
            var index = cmd.GetFullSystemVectorDescription()._items.FindIndex(i => i.Descriptor == $"overPressure_{level}");
            var time = new DateTime(2026, 1, 1);
            foreach (var (minute, pressure, shouldEmit) in new[]
            {
                (0, 2d, true), (1, 2d, false), (2, 1.5, false), (3, 2d, false),
                (6, 2d, false), (7, 1.5, false), (8, 2d, true)
            })
            {
                DataVector? vector = null;
                cmd.MakeDecision([new("pressure", pressure)], time.AddMinutes(minute), ref vector, []);
                Assert.AreEqual(pressure > 1.5 ? 1d : 0d, vector[index]);
                Assert.IsNull(cmd.DequeueEvents(), "Calculating channels must not emit events or run commands.");
                var previousExecutions = executions;
                await ReceiveVector(cmd, vector);
                var events = cmd.DequeueEvents() ?? [];
                Assert.HasCount(shouldEmit ? 2 : 0, events, $"Events at minute {minute}");
                Assert.AreEqual(previousExecutions + (shouldEmit ? 1 : 0), executions);
                if (shouldEmit)
                {
                    var alertEvent = events.Single(e => e.EventType == (byte)eventType);
                    Assert.AreEqual(" overPressure (pressure) > 1.5 (2)", alertEvent.Data);
                    Assert.AreEqual("hej", events.Single(e => e.EventType == (byte)EventType.Command).Data);
                }
            }
        }

        [DataRow(1.5, 0d)]
        [DataRow(2d, 1d)]
        [TestMethod]
        public async Task FinalSafetyDecisionDeterminesChannelInLiveExecutionAndReplay(double finalPressure, double expected)
        {
            var config = new IOconfFile(["Alert;overPressure;pressure > 1.5;0;hej"]);
            using var cmd = CreateHandler(config, "pressure");
            _ = new Alerts(config, cmd);
            var executions = 0;
            cmd.AddCommand("hej", _ => { executions++; return true; });
            cmd.AddDecisions([new SetPressureDecision("normal", 3)]);
            cmd.AddSafetyDecisions([new SetPressureDecision("firstSafety", 4), new SetPressureDecision("lastSafety", finalPressure)]);
            var desc = cmd.GetFullSystemVectorDescription();
            var index = desc._items.FindIndex(i => i.Descriptor == "overPressure_alert");
            DataVector? live = null;
            cmd.MakeDecision([new("pressure", 0)], new DateTime(2026, 1, 1), ref live, []);
            Assert.AreEqual(expected, live[index]);

            live.Data[index] = 1 - expected;
            var replay = new DataVector(new double[desc.Length], live.Timestamp);
            cmd.MakeDecisionUsingInputsFromNewVector(live, replay, []);
            Assert.AreEqual(expected, replay[index]);
            Assert.IsNull(cmd.DequeueEvents());
            Assert.AreEqual(0, executions);
            await ReceiveVector(cmd, replay);
            Assert.HasCount(expected == 1 ? 2 : 0, cmd.DequeueEvents() ?? []);
            Assert.AreEqual(expected == 1 ? 1 : 0, executions);
        }

        [TestMethod]
        public async Task InvalidReadingsClearChannelWithoutChangingEventHistory()
        {
            var config = new IOconfFile(["Alert;overPressure;pressure > 1.5;0"]);
            using var cmd = CreateHandler(config, "pressure");
            _ = new Alerts(config, cmd);
            var index = cmd.GetFullSystemVectorDescription()._items.FindIndex(i => i.Descriptor == "overPressure_alert");
            var time = new DateTime(2026, 1, 1);
            foreach (var (pressure, expected, emits) in new[]
            {
                (10000d, 0d, false), (2d, 1d, true), (10001d, 0d, false),
                (2d, 1d, false), (1.5, 0d, false), (2d, 1d, true)
            })
            {
                DataVector? vector = null;
                cmd.MakeDecision([new("pressure", pressure)], time, ref vector, []);
                Assert.AreEqual(expected, vector[index]);
                await ReceiveVector(cmd, vector);
                Assert.HasCount(emits ? 1 : 0, cmd.DequeueEvents() ?? []);
                time = time.AddSeconds(1);
            }
        }

        [DataRow("=", 0d)]
        [DataRow("!=", 1d)]
        [DataRow(">", 0d)]
        [DataRow("<", 0d)]
        [DataRow(">=", 0d)]
        [DataRow("<=", 0d)]
        [TestMethod]
        public async Task NaNRetainsExistingComparisonSemantics(string comparison, double expected)
        {
            var config = new IOconfFile([$"Alert;overPressure;pressure {comparison} 1.5"]);
            using var cmd = CreateHandler(config, "pressure");
            _ = new Alerts(config, cmd);
            var index = cmd.GetFullSystemVectorDescription()._items.FindIndex(i => i.Descriptor == "overPressure_alert");
            DataVector? vector = null;
            cmd.MakeDecision([new("pressure", double.NaN)], new DateTime(2026, 1, 1), ref vector, []);
            Assert.AreEqual(expected, vector[index]);
            await ReceiveVector(cmd, vector);
            Assert.HasCount((int)expected, cmd.DequeueEvents() ?? []);
        }

        [DataRow("overPressure_alert")]
        [DataRow("OVERPRESSURE_ALERT")]
        [TestMethod]
        public void GeneratedChannelRejectsExistingFieldCollision(string existingField)
        {
            var config = new IOconfFile(["Alert;overPressure;pressure > 1.5", $"Math;{existingField};0"]);
            using var cmd = CreateHandler(config, "pressure");
            var ex = Assert.Throws<FormatException>(() => cmd.GetFullSystemVectorDescription());
            StringAssert.Contains(ex.Message, "Different fields cannot use the same name");
            StringAssert.Contains(ex.Message, existingField);
        }

        [TestMethod]
        public void MissingSourceFailsWhenBuildingVector()
        {
            using var cmd = CreateHandler(new IOconfFile(["Alert;overPressure;missing > 1.5"]));
            var ex = Assert.Throws<FormatException>(() => cmd.GetFullSystemVectorDescription());
            StringAssert.Contains(ex.Message, "overPressure points to missing vector field: missing");
        }

        [TestMethod]
        public async Task AutomaticAlertAndErrorChannelsStillEmitButInfoDoesNot()
        {
            var config = new IOconfFile([]);
            using var cmd = CreateHandler(config, "device_alert", "device_error", "device_info");
            CALog.LoggerForUserOutput = new CALog.EventsLogger(config, cmd);
            _ = new Alerts(config, cmd);
            DataVector? vector = null;
            cmd.MakeDecision([new("device_alert", 1), new("device_error", 1), new("device_info", 1)], new DateTime(2026, 1, 1), ref vector, []);
            await ReceiveVector(cmd, vector);
            var events = cmd.DequeueEvents() ?? [];
            Assert.HasCount(2, events);
            Assert.AreEqual(" device_alert (device_alert) = 1 (1)", events.Single(e => e.EventType == (byte)EventType.Alert).Data);
            Assert.AreEqual(" device_error (device_error) = 1 (1)", events.Single(e => e.EventType == (byte)EventType.LogError).Data);
        }

        [TestMethod]
        public async Task ReenablingAlertsRetainsLeaderChangeRetriggering()
        {
            var config = new IOconfFile(["Alert;overPressure;pressure > 1.5"]);
            using var cmd = CreateHandler(config, "pressure");
            var alerts = new Alerts(config, cmd) { Disabled = true };
            var index = cmd.GetFullSystemVectorDescription()._items.FindIndex(i => i.Descriptor == "overPressure_alert");
            for (int cycle = 0; cycle < 3; cycle++)
            {
                DataVector? vector = null;
                cmd.MakeDecision([new("pressure", 2)], new DateTime(2026, 1, 1).AddSeconds(cycle), ref vector, []);
                Assert.AreEqual(1d, vector[index]);
                await ReceiveVector(cmd, vector);
                Assert.HasCount(cycle == 0 ? 0 : 1, cmd.DequeueEvents() ?? []);
                alerts.Disabled = false;
            }
        }

        private sealed class SetPressureDecision(string name, double pressure) : Plugin.LoopControlDecision
        {
            private int index;
            public override string Name => name;
            public override Plugin.PluginField[] PluginFields => [];
            public override string[] HandledEvents => [];
            public override void Initialize(Plugin.VectorDescription desc) => index = Enumerable.Range(0, desc.Count).Single(i => desc[i] == "pressure");
            public override void MakeDecision(Plugin.DataVector vector, List<string> events) => vector[index] = pressure;
        }

        private static async Task ReceiveVector(CommandHandler cmd, DataVector vector)
        {
            cmd.OnNewVectorReceived(vector);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (cmd.LatestVectorTimeProcessedByAllReaders() != vector.Timestamp)
                await Task.Delay(1, timeout.Token);
        }

        private static CommandHandler CreateHandler(IOconfFile config, params string[] inputs)
        {
            var cmd = new CommandHandler(config, runCommandLoop: false, logger: new FullDecisionTestContext.ChannelLogger());
            var subsystem = new Mock<ISubsystemWithVectorData>();
            subsystem.Setup(s => s.GetVectorDescriptionItems()).Returns(new SubsystemDescriptionItems([])
            {
                GlobalInputs = inputs.Select(name => new VectorDescriptionItem("double", name, DataTypeEnum.Input)).ToList()
            });
            cmd.AddSubsystem(subsystem.Object);
            return cmd;
        }
    }
}
