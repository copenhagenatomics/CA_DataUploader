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
        [DataRow(";level:alert", "alert")]
        [DataRow(";level:error", "error")]
        [DataRow(";level:info", "info")]
        [TestMethod]
        public void ChannelTracksConditionOnEveryCycle(string severity, string level)
        {
            var config = new IOconfFile([$"Alert;overPressure;pressure > 1.5;5{severity}"]);
            using var cmd = CreateHandler(config, "pressure");
            _ = new Alerts(config, cmd);
            var field = cmd.GetFullSystemVectorDescription()._items.Single(i => i.Descriptor == $"overPressure_{level}");
            Assert.AreEqual(DataTypeEnum.State, field.DirectionType);
            Assert.IsTrue(field.Upload);
            CollectionAssert.Contains(config.GetEntries<IOconfRow>().SelectMany(row => row.GetExpandedNames(config)).ToArray(), field.Descriptor);
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

        [DataRow(";5;emergencyshutdown", "alert", EventType.Alert, 5, true)]
        [DataRow(";5;emergencyshutdown;level:alert", "alert", EventType.Alert, 5, true)]
        [DataRow(";5;emergencyshutdown;level:error", "error", EventType.LogError, 5, true)]
        [DataRow(";5;emergencyshutdown;level:info", "info", EventType.Log, 5, true)]
        [DataRow(";level:error", "error", EventType.LogError, 30, false)]
        [DataRow(";5;level:error", "error", EventType.LogError, 5, false)]
        [DataRow(";emergencyshutdown;level:error", "error", EventType.LogError, 30, true)]
        [DataRow(";5;emergencyshutdown;level: error", "error", EventType.LogError, 5, true)]
        [DataRow(";Level: error;5;emergencyshutdown", "error", EventType.LogError, 5, true)]
        [DataRow(";5;emergencyshutdown;level:error;tags:pressure", "error", EventType.LogError, 5, true)]
        [DataRow("", "alert", EventType.Alert, 30, false)]
        [DataRow(";emergencyshutdown", "alert", EventType.Alert, 30, true)]
        [TestMethod]
        public async Task ActivationEmitsOnceAndRateLimitsEventsAndCommands(string fields, string level, EventType eventType, int cooldown, bool hasCommand)
        {
            var config = new IOconfFile([$"Alert;overPressure;pressure > 1.5{fields}"]);
            using var cmd = CreateHandler(config, "pressure");
            CALog.LoggerForUserOutput = new CALog.EventsLogger(config, cmd);
            _ = new Alerts(config, cmd);
            var executions = 0;
            cmd.AddCommand("emergencyshutdown", _ => { executions++; return true; });
            var index = cmd.GetFullSystemVectorDescription()._items.FindIndex(i => i.Descriptor == $"overPressure_{level}");
            var time = new DateTime(2026, 1, 1);
            foreach (var (minute, pressure, shouldEmit) in new[]
            {
                (0, 2d, true), (1, 2d, false), (cooldown - 2, 1.5, false), (cooldown - 1, 2d, false),
                (cooldown + 1, 2d, false), (cooldown + 2, 1.5, false), (cooldown + 3, 2d, true)
            })
            {
                DataVector? vector = null;
                cmd.MakeDecision([new("pressure", pressure)], time.AddMinutes(minute), ref vector, []);
                Assert.AreEqual(pressure > 1.5 ? 1d : 0d, vector[index]);
                Assert.IsNull(cmd.DequeueEvents(), "Calculating channels must not emit events or run commands.");
                var previousExecutions = executions;
                await ReceiveVector(cmd, vector);
                var events = cmd.DequeueEvents() ?? [];
                Assert.HasCount(shouldEmit ? (hasCommand ? 2 : 1) : 0, events, $"Events at minute {minute}");
                Assert.AreEqual(previousExecutions + (shouldEmit && hasCommand ? 1 : 0), executions);
                if (shouldEmit)
                {
                    var alertEvent = events.Single(e => e.EventType == (byte)eventType);
                    Assert.AreEqual(" overPressure (pressure) > 1.5 (2)", alertEvent.Data);
                    if (hasCommand)
                        Assert.AreEqual("emergencyshutdown", events.Single(e => e.EventType == (byte)EventType.Command).Data);
                }
            }
        }

        [DataRow("Sensorx", "Sensorx=123", 122d, 123d, 123d)]
        [DataRow("Sensorx", "Sensorx=193.123", 193.122d, 193.123d, 193.123d)]
        [DataRow("Sensorx", "Sensorx>123", 123d, 124d, 123.00012d)]
        [DataRow("Sensorx", "Sensorx>=123", 122.999d, 123d, 124d)]
        [DataRow("Sensorx", "Sensorx<=123", 123.001d, 123d, 122d)]
        [DataRow("Sensorx", "Sensorx<123", 123d, 122d, 121d)]
        [DataRow("Sensorx", "Sensorx < 123", 123.001d, 121d, 122d)]
        [DataRow("Sensorx", "Sensorx != 123", 123d, 122.999d, 124d)]
        [DataRow("OxygenOut_Oxygen%", "OxygenOut_Oxygen%>1", 1d, 2d, 3d)]
        [TestMethod]
        public async Task ComparisonsTrackChannelsAndEmitOnlyOnActivation(string sensor, string condition, double inactive, double active, double stillActive)
        {
            var config = new IOconfFile([$"Alert;comparison;{condition};0"]);
            using var cmd = CreateHandler(config, sensor);
            _ = new Alerts(config, cmd);
            var index = cmd.GetFullSystemVectorDescription()._items.FindIndex(i => i.Descriptor == "comparison_alert");
            var time = new DateTime(2026, 1, 1);
            foreach (var (value, state, emits) in new[]
            {
                (active, 1d, true), (stillActive, 1d, false), (inactive, 0d, false), (active, 1d, true)
            })
            {
                DataVector? vector = null;
                cmd.MakeDecision([new(sensor, value)], time, ref vector, []);
                await ReceiveVector(cmd, vector);

                Assert.AreEqual(state, vector[index]);
                Assert.HasCount(emits ? 1 : 0, cmd.DequeueEvents() ?? []);
                time = time.AddSeconds(1);
            }
        }

        [DataRow("Sensorx = 123", 123d)]
        [DataRow("Sensorx > 123", 123.00012d)]
        [DataRow("Sensorx >= 123", 123d)]
        [DataRow("Sensorx <= 123", 122d)]
        [TestMethod]
        public async Task ValidReadingAfterInitialNaNStillEmitsAlert(string condition, double value)
        {
            var config = new IOconfFile([$"Alert;recovered;{condition}"]);
            using var cmd = CreateHandler(config, "Sensorx");
            _ = new Alerts(config, cmd);
            DataVector? vector = null;
            var time = new DateTime(2026, 1, 1);
            cmd.MakeDecision([new("Sensorx", double.NaN)], time, ref vector, []);
            await ReceiveVector(cmd, vector);
            cmd.DequeueEvents();

            cmd.MakeDecision([new("Sensorx", value)], time.AddSeconds(1), ref vector, []);
            await ReceiveVector(cmd, vector);

            var events = cmd.DequeueEvents() ?? [];
            Assert.HasCount(1, events);
            Assert.AreEqual((byte)EventType.Alert, events[0].EventType);
        }

        [DataRow("Sensorx = 123", 122d, 123d, " MyName (Sensorx) = 123 (123)")]
        [DataRow("Sensorx > 123", 123d, 123.00012d, " MyName (Sensorx) > 123 (123.00012)")]
        [TestMethod]
        public async Task AlertEventIncludesConditionAndReading(string condition, double oldValue, double value, string expectedMessage)
        {
            var config = new IOconfFile([$"Alert;MyName;{condition}"]);
            using var cmd = CreateHandler(config, "Sensorx");
            _ = new Alerts(config, cmd);
            DataVector? vector = null;
            var time = new DateTime(2026, 1, 1);
            cmd.MakeDecision([new("Sensorx", oldValue)], time, ref vector, []);
            await ReceiveVector(cmd, vector);

            cmd.MakeDecision([new("Sensorx", value)], time.AddSeconds(1), ref vector, []);
            await ReceiveVector(cmd, vector);

            var events = cmd.DequeueEvents() ?? [];
            Assert.HasCount(1, events);
            Assert.AreEqual(expectedMessage, events[0].Data);
        }

        [DataRow(1.5, 0d)]
        [DataRow(2d, 1d)]
        [TestMethod]
        public async Task FinalSafetyDecisionDeterminesChannelInLiveExecutionAndReplay(double finalPressure, double expected)
        {
            var config = new IOconfFile(["Alert;overPressure;pressure > 1.5;0;emergencyshutdown"]);
            using var cmd = CreateHandler(config, "pressure");
            _ = new Alerts(config, cmd);
            var executions = 0;
            cmd.AddCommand("emergencyshutdown", _ => { executions++; return true; });
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
        public async Task LargeReadingsUpdateChannelWithoutChangingEventHistory()
        {
            var config = new IOconfFile(["Alert;overPressure;pressure > 1.5;0"]);
            using var cmd = CreateHandler(config, "pressure");
            _ = new Alerts(config, cmd);
            var index = cmd.GetFullSystemVectorDescription()._items.FindIndex(i => i.Descriptor == "overPressure_alert");
            var time = new DateTime(2026, 1, 1);
            foreach (var (pressure, expected, emits) in new[]
            {
                (10000d, 1d, false), (2d, 1d, true), (10001d, 1d, false),
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

        [DataRow("overPressure_alert")]
        [DataRow("OVERPRESSURE_ALERT")]
        [TestMethod]
        public void GeneratedChannelRejectsExistingFieldCollision(string existingField)
        {
            var config = new IOconfFile(["Alert;overPressure;pressure > 1.5", $"Math;{existingField};0"]);
            using var cmd = CreateHandler(config, "pressure");
            _ = new Alerts(config, cmd);
            var ex = Assert.Throws<FormatException>(() => cmd.GetFullSystemVectorDescription());
            StringAssert.Contains(ex.Message, "Different fields cannot use the same name");
            StringAssert.Contains(ex.Message, existingField);
        }

        [DataRow("level:warning")]
        [DataRow("level:")]
        [DataRow("level:error level:info")]
        [DataRow("level:info;level:error")]
        [DataRow("level:alert;level:alert")]
        [TestMethod]
        public void ConfigurationRejectsInvalidOrRepeatedLevels(string fields)
        {
            var ex = Assert.Throws<FormatException>(() => new IOconfFile([$"Alert;overPressure;pressure > 1.5;{fields}"]));
            StringAssert.Contains(ex.Message, "level");
        }

        [DataRow("Alert;MyName;Sensorx;=;123", DisplayName = "old format - no longer supported")]
        [DataRow("Alert;MyName;Sensorx = ")]
        [DataRow("Alert;MyName;Sensorx =")]
        [DataRow("Alert;MyName;Sensorx > abc")]
        [DataRow("Alert;MyName;Sensorx")]
        [DataRow("Alert;MyName;Sensorx <= 123,2")]
        [TestMethod]
        public void ConfigurationRejectsInvalidConditions(string row)
        {
            var ex = Assert.Throws<FormatException>(() => new IOconfFile([row]));
            StringAssert.Contains(ex.Message, row.Trim());
        }

        [TestMethod]
        public void MissingSourceFailsWhenBuildingVector()
        {
            var config = new IOconfFile(["Alert;overPressure;missing > 1.5"]);
            using var cmd = CreateHandler(config);
            _ = new Alerts(config, cmd);
            var ex = Assert.Throws<FormatException>(() => cmd.GetFullSystemVectorDescription());
            StringAssert.Contains(ex.Message, "overPressure points to missing vector field: missing");
        }

        [DataRow("overPressure")]
        [DataRow("overPressure_alert")]
        [TestMethod]
        public void AlertNamesDoNotAllowUnknownConfigurationLines(string rowType)
        {
            var config = new IOconfFile(["Alert;overPressure;pressure > 1.5", $"{rowType};somename;somevalue"]);
            using var cmd = CreateHandler(config, "pressure");
            _ = new Alerts(config, cmd);

            var ex = Assert.Throws<NotSupportedException>(() => cmd.GetFullSystemVectorDescription());

            StringAssert.Contains(ex.Message, rowType);
            StringAssert.Contains(ex.Message, "somename");
        }

        [DataRow(false)]
        [DataRow(true)]
        [TestMethod]
        public void AlertDecisionRejectsDuplicateDecisionName(bool safetyDecision)
        {
            var config = new IOconfFile(["Alert;overPressure;pressure > 1.5"]);
            using var cmd = CreateHandler(config, "pressure");
            var decision = new SetPressureDecision("overPressure_alert", 2);
            if (safetyDecision)
                cmd.AddSafetyDecisions([decision]);
            else
                cmd.AddDecisions([decision]);
            _ = new Alerts(config, cmd);

            var ex = Assert.Throws<FormatException>(() => cmd.GetFullSystemVectorDescription());

            StringAssert.Contains(ex.Message, "Duplicate decision names");
            StringAssert.Contains(ex.Message, "overPressure_alert");
        }

        [TestMethod]
        public async Task AutomaticAlertErrorAndInfoChannelsEmitEvents()
        {
            var config = new IOconfFile([]);
            using var cmd = CreateHandler(config, "device_alert", "device_error", "device_info");
            CALog.LoggerForUserOutput = new CALog.EventsLogger(config, cmd);
            _ = new Alerts(config, cmd);
            DataVector? vector = null;
            cmd.MakeDecision([new("device_alert", 1), new("device_error", 1), new("device_info", 1)], new DateTime(2026, 1, 1), ref vector, []);
            await ReceiveVector(cmd, vector);
            var events = cmd.DequeueEvents() ?? [];
            Assert.HasCount(3, events);
            Assert.AreEqual(" device_alert (device_alert) = 1 (1)", events.Single(e => e.EventType == (byte)EventType.Alert).Data);
            Assert.AreEqual(" device_error (device_error) = 1 (1)", events.Single(e => e.EventType == (byte)EventType.LogError).Data);
            Assert.AreEqual(" device_info (device_info) = 1 (1)", events.Single(e => e.EventType == (byte)EventType.Log).Data);
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
