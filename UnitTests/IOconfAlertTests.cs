using CA_DataUploaderLib;
using CA_DataUploaderLib.IOconf;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;

namespace UnitTests
{
    [TestClass]
    public class IOconfAlertTests
    {
        [DataRow("", "alert", EventType.Alert)]
        [DataRow(";level:alert", "alert", EventType.Alert)]
        [DataRow(";level:error", "error", EventType.LogError)]
        [DataRow(";level:info", "info", EventType.Log)]
        [TestMethod]
        public void SeveritySelectsEventTypeAndExpandedChannel(string severity, string level, EventType eventType)
        {
            var config = new IOconfFile([$"Alert;overPressure;pres_abs_bar > 1.5;5;emergencyshutdown{severity}"]);
            var alert = config.GetAlerts().Single();

            Assert.AreEqual(eventType, alert.EventType);
            CollectionAssert.AreEqual(new[] { $"overPressure_{level}" }, alert.GetExpandedNames(config).ToArray());
            Assert.AreEqual(5, alert.RateLimitMinutes);
            Assert.AreEqual("emergencyshutdown", alert.Command);
        }

        [DataRow("level:warning")]
        [DataRow("level:")]
        [DataRow("level:error level:info")]
        [DataRow("level:info;level:error")]
        [DataRow("level:alert;level:alert")]
        [TestMethod]
        public void SeverityRejectsInvalidOrRepeatedFields(string fields)
        {
            var ex = Assert.Throws<FormatException>(() => new IOconfFile([$"Alert;overPressure;pressure > 1.5;{fields}"]));
            StringAssert.Contains(ex.Message, "level");
        }

        [DataRow("level:error", 30, null)]
        [DataRow("5;level:error", 5, null)]
        [DataRow("emergencyshutdown;level:error", 30, "emergencyshutdown")]
        [DataRow("5;emergencyshutdown;level: error", 5, "emergencyshutdown")]
        [DataRow("Level: error;5;emergencyshutdown", 5, "emergencyshutdown")]
        [DataRow("5;emergencyshutdown;level:error;tags:pressure", 5, "emergencyshutdown")]
        [TestMethod]
        public void SeverityFieldPreservesOptionalArguments(string fields, int cooldown, string? command)
        {
            var config = new IOconfFile([$"Alert;overPressure;pressure > 1.5;{fields}"]);
            var alert = config.GetAlerts().Single();

            Assert.AreEqual(EventType.LogError, alert.EventType);
            Assert.AreEqual(cooldown, alert.RateLimitMinutes);
            Assert.AreEqual(command, alert.Command);
        }

        [DataRow("", null)]
        [DataRow(";emergencyshutdown", "emergencyshutdown")]
        [TestMethod]
        public void LegacyDefaultsRemainAvailable(string command, string? expectedCommand)
        {
            var alert = new IOconfAlert($"Alert;overPressure;pressure > 1.5{command}", 0);
            Assert.AreEqual(30, alert.RateLimitMinutes);
            Assert.AreEqual(expectedCommand, alert.Command);
            Assert.AreEqual(EventType.Alert, alert.EventType);
        }

        [DataRow("Alert;MyName;Sensorx=123", 123d)]
        [DataRow("Alert;MyName;Sensorx=193.123", 193.123d)]
        [DataRow("Alert;MyName;Sensorx>123", 123.00012d)]
        [DataRow("Alert;MyName;Sensorx>123", 124d)]
        [DataRow("Alert;MyName;Sensorx>=23", 123d)]
        [DataRow("Alert;MyName;Sensorx<=123", 123d)]
        [DataRow("Alert;MyName;Sensorx<=123", 122d)]
        [DataRow("Alert;MyName;Sensorx<123", 122d)]
        [DataRow("Alert;MyName;Sensorx < 123", 122d)]
        [DataRow("Alert;OxygenRaised;OxygenOut_Oxygen%>1", 2d)]
        [TestMethod]
        public void AlertTriggers(string row, double value) 
        {
            var alert = new IOconfAlert(row, 0);
            Assert.IsTrue(alert.CheckValue(value, DateTime.UtcNow));
        }

        [DataRow("Alert;MyName;Sensorx = 123", 122d, 123d)]
        [DataRow("Alert;MyName;Sensorx = 193.123", 193.122d, 193.123d)]
        [DataRow("Alert;MyName;Sensorx > 123", 123d, 123.00012d)]
        [DataRow("Alert;MyName;Sensorx > 123", 123d, 124d)]
        [DataRow("Alert;MyName;Sensorx >= 123", 122.999d, 123d)]
        [DataRow("Alert;MyName;Sensorx <= 123", 123.001d, 123d)]
        [DataRow("Alert;MyName;Sensorx <= 123", 123.001d, 122d)]
        [DataRow("Alert;MyName;Sensorx < 123", 123.001d, 122d)]
        [DataRow("Alert;MyName;Sensorx != 123", 123d, 122.999d)]
        [DataRow("Alert;MyName;Sensorx = 123", double.NaN, 123d)]
        [DataRow("Alert;MyName;Sensorx > 123", double.NaN, 123.00012d)]
        [DataRow("Alert;MyName;Sensorx >= 123", double.NaN, 123d)]
        [DataRow("Alert;MyName;Sensorx <= 123", double.NaN, 122d)]
        [TestMethod]
        public void AlertTriggersWhenOldValueDidNotMatch(string row, double oldValue, double value)
        {
            var alert = new IOconfAlert(row, 0);
            alert.CheckValue(oldValue, DateTime.UtcNow);
            Assert.IsTrue(alert.CheckValue(value, DateTime.UtcNow));
        }

        [DataRow("Alert;MyName;Sensorx = 123", 122d, 123d, " MyName (Sensorx) = 123 (123)")]
        [DataRow("Alert;MyName;Sensorx > 123", 123d, 123.00012d, " MyName (Sensorx) > 123 (123.00012)")]
        [TestMethod]
        public void AlertReturnsExpectedMessageAfterCheckingValueTwice(string row, double oldValue, double value, string expectedMessage)
        {
            var alert = new IOconfAlert(row, 0);
            alert.CheckValue(oldValue, DateTime.UtcNow);
            alert.CheckValue(value, DateTime.UtcNow);
            Assert.AreEqual(expectedMessage, alert.Message);
        }

        [DataRow("Alert;MyName;Sensorx=123", 123d, 123d)]
        [DataRow("Alert;MyName;Sensorx=193.123", 193.123d, 193.123d)]
        [DataRow("Alert;MyName;Sensorx>123", 124d, 123.00012d)]
        [DataRow("Alert;MyName;Sensorx>123", 123.001d, 124d)]
        [DataRow("Alert;MyName;Sensorx>=123", 123d, 123d)]
        [DataRow("Alert;MyName;Sensorx<=123", 122.999d, 123d)]
        [DataRow("Alert;MyName;Sensorx<=123", 122d, 122d)]
        [DataRow("Alert;MyName;Sensorx<123", 121d, 122d)]
        [TestMethod]
        public void AlertDoesNotTriggersWhenOldValueMatched(string row, double oldValue, double value)
        {
            var alert = new IOconfAlert(row, 0);
            alert.CheckValue(oldValue, DateTime.UtcNow);
            Assert.IsFalse(alert.CheckValue(value, DateTime.UtcNow));
        }

        [DataRow("Alert;MyName;Sensorx;=;123", DisplayName = "old format - no longer supported")]
        [DataRow("Alert;MyName;Sensorx = ")]
        [DataRow("Alert;MyName;Sensorx =")]
        [DataRow("Alert;MyName;Sensorx > abc")]
        [DataRow("Alert;MyName;Sensorx")]
        [DataRow("Alert;MyName;Sensorx <= 123,2")]//thousands separator is not allowed so this does not get interpreted as 1232
        [TestMethod]
        public void AlertRejectsInvalidConfiguration(string row)
        {
            var ex = Assert.Throws<FormatException>(() => new IOconfAlert(row, 0));
            Assert.AreEqual($"IOconfAlert: wrong format: {row}. Format: Alert;Name;SensorName comparison value;[rateMinutes];[command];[level:alert|error|info]. Supported comparisons: =, !=, >, <, >=, <=", ex.Message);
        }
    }
}
