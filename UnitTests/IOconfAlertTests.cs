using CA_DataUploaderLib.IOconf;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace UnitTests
{
    [TestClass]
    public class IOconfAlertTests
    {
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
        [DataTestMethod]
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
        [DataTestMethod]
        public void AlertTriggersWhenOldValueDidNotMatch(string row, double oldValue, double value)
        {
            var alert = new IOconfAlert(row, 0);
            alert.CheckValue(oldValue, DateTime.UtcNow);
            Assert.IsTrue(alert.CheckValue(value, DateTime.UtcNow));
        }

        [DataRow("Alert;MyName;Sensorx = 123", 122d, 123d, " MyName (Sensorx) = 123 (123)")]
        [DataRow("Alert;MyName;Sensorx > 123", 123d, 123.00012d, " MyName (Sensorx) > 123 (123.00012)")]
        [DataTestMethod]
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
        [DataTestMethod]
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
        [DataTestMethod]
        public void AlertRejectsInvalidConfiguration(string row)
        {
            var ex = Assert.ThrowsException<Exception>(() => new IOconfAlert(row, 0));
            Assert.AreEqual($"IOconfAlert: wrong format: {row}. Format: Alert;Name;SensorName comparison value;[rateMinutes];[command]. Supported comparisons: =,!=, >, <, >=, <=", ex.Message);
        }

        [DataRow("Sensorx > 123", 124d, 30, null)]
        [DataRow("Sensorx > 123 20", 124d, 20, null)]
        [DataRow("Sensorx > 123 20 oven 0", 124d, 20, "oven 0")]
        [DataRow("Sensorx > 123 oven 10", 124d, 30, "oven 10")]
        [DataTestMethod]
        public void SupportsDynamicAlertsFormat(string expressionWithOptions, double value, int rateLimit, string command)
        {
            var alert = new IOconfAlert("dynamicalert", expressionWithOptions);
            Assert.IsTrue(alert.CheckValue(value, DateTime.UtcNow));
            Assert.AreEqual(rateLimit, alert.RateLimitMinutes);
            Assert.AreEqual(command, alert.Command);
        }
    }
}
