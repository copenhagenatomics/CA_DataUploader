using CA_DataUploaderLib.IOconf;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace UnitTests
{
    [TestClass]
    public class IOconfAlertTests
    {
        [DataRow("Alert;MyName;=;123", 123d)]
        [DataRow("Alert;MyName;=;193.123", 193.123d)]
        [DataRow("Alert;MyName;>;123", 123.00012d)]
        [DataRow("Alert;MyName;>;123", 124d)]
        [DataRow("Alert;MyName;>=;123", 123d)]
        [DataRow("Alert;MyName;nan", double.NaN)]
        [DataRow("Alert;MyName;int", 123d)]
        [DataRow("Alert;MyName;int", 122d)]
        [DataRow("Alert;MyName;int", 100000d)]
        [DataRow("Alert;MyName;int", 10000000d)]
        [DataRow("Alert;MyName;int", 1d)]
        [DataRow("Alert;MyName;int", -1d)]
        [DataRow("Alert;MyName;int", -10000000d)]
        [DataTestMethod]
        public void AlertTriggers(string row, double value) 
        {
            var alert = new IOconfAlert(row, 0);
            Assert.IsTrue(alert.CheckValue(value));
        }

        [DataRow("Alert;MyName;=;123", 122d, 123d)]
        [DataRow("Alert;MyName;=;193.123", 193.122d, 193.123d)]
        [DataRow("Alert;MyName;>;123", 123d, 123.00012d)]
        [DataRow("Alert;MyName;>;123", 123d, 124d)]
        [DataRow("Alert;MyName;>=;123", 122.999d, 123d)]
        [DataRow("Alert;MyName;<=;123", 123.001d, 123d)]
        [DataRow("Alert;MyName;<=;123", 123.001d, 122d)]
        [DataRow("Alert;MyName;<;123", 123.001d, 122d)]
        [DataRow("Alert;MyName;!=;123", 123d, 122.999d)]
        [DataRow("Alert;MyName;nan", 123d, double.NaN)]
        [DataRow("Alert;MyName;=;123", double.NaN, 123d)]
        [DataTestMethod]
        public void AlertTriggersWhenOldValueDidNotMatch(string row, double oldValue, double value)
        {
            var alert = new IOconfAlert(row, 0);
            alert.CheckValue(oldValue);
            Assert.IsTrue(alert.CheckValue(value));
        }

        [DataRow("Alert;MyName;=;123", 122d, 123d, " MyName = 123 (123)")]
        [DataRow("Alert;MyName;>;123", 123d, 123.00012d, " MyName > 123 (123.00012)")]
        [DataRow("Alert;MyName;nan", 123d, double.NaN, " MyName is not a number (NaN)")]
        [DataRow("Alert;MyName;int", 123.123d, 123d, " MyName is an integer (123)")]
        [DataTestMethod]
        public void AlertReturnsExpectedMessageAfterCheckingValueTwice(string row, double oldValue, double value, string expectedMessage)
        {
            var alert = new IOconfAlert(row, 0);
            alert.CheckValue(oldValue);
            alert.CheckValue(value);
            Assert.AreEqual(expectedMessage, alert.Message);
        }

        [DataRow("Alert;MyName;=;123", 123d, 123d)]
        [DataRow("Alert;MyName;=;193.123", 193.123d, 193.123d)]
        [DataRow("Alert;MyName;>;123", 124d, 123.00012d)]
        [DataRow("Alert;MyName;>;123", 123.001d, 124d)]
        [DataRow("Alert;MyName;>=;123", 123d, 123d)]
        [DataRow("Alert;MyName;<=;123", 122.999d, 123d)]
        [DataRow("Alert;MyName;<=;123", 122d, 122d)]
        [DataRow("Alert;MyName;<;123", 121d, 122d)]
        [DataRow("Alert;MyName;nan", double.NaN, double.NaN)]
        [DataTestMethod]
        public void AlertDoesNotTriggersWhenOldValueMatched(string row, double oldValue, double value)
        {
            var alert = new IOconfAlert(row, 0);
            alert.CheckValue(oldValue);
            Assert.IsFalse(alert.CheckValue(value));
        }

        [DataRow("Alert;MyName;=;")]
        [DataRow("Alert;MyName;=")]
        [DataRow("Alert;MyName;>;abc")]
        [DataRow("Alert;MyName")]
        [DataRow("Alert;MyName;<=;123,2")]//thousands separator is not allowed so this does not get interpreted as 1232
        [DataTestMethod]
        public void AlertRejectsInvalidConfiguration(string row)
        {
            var ex = Assert.ThrowsException<Exception>(() => new IOconfAlert(row, 0));
            Assert.AreEqual($"IOconfAlert: wrong format: {row}", ex.Message);
        }
    }
}
