using CA_DataUploaderLib.IOconf;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace UnitTests
{
    [TestClass]
    public class IOconfOvenTests
    {
        [DataRow("Oven;2;topheater1;typek1;0.2;00:00:10;20", "topheater1", "typek1", 0.2d, 10d, 0.2d)]
        [DataTestMethod]
        public void ParsesOvenLine(string row, string heaterName, string sensorName, double pgain, double controlPeriodSeconds, double maxOutput) 
        {
            var oven = new IOconfOven(row, 0);
            Assert.AreEqual(heaterName, oven.HeaterName);
            Assert.AreEqual(sensorName, oven.TemperatureSensorName);
            Assert.AreEqual(pgain, oven.ProportionalGain);
            Assert.AreEqual(TimeSpan.FromSeconds(controlPeriodSeconds), oven.ControlPeriod);
            Assert.AreEqual(maxOutput, oven.MaxOutputPercentage);
        }

        [DataRow("OvenProportionalControlUpdates;3.5;00:01:10;30", 3.5d, 70d, 0.3d)]
        [DataRow("OvenProportionalControlUpdates;3;00:00:05;5", 3d, 5d, 0.05d)]
        [DataTestMethod]
        public void ParsesOvenProportionalControlUpdatesLine(string row, double pgain, double controlPeriodSeconds, double maxOutput)
        {
            var oven = new IOconfOvenProportionalControlUpdates(row, 0);
            Assert.AreEqual(pgain, oven.MaxProportionalGain);
            Assert.AreEqual(TimeSpan.FromSeconds(controlPeriodSeconds), oven.MaxControlPeriod);
            Assert.AreEqual(maxOutput, oven.MaxOutputPercentage);
        }

        [DataRow("OvenProportionalControlUpdates;3.5a;00:01:10;30")]
        [DataRow("OvenProportionalControlUpdates;3;00:00:05;5d")]
        [DataRow("OvenProportionalControlUpdates;3;00:00:05;-5")]
        [DataTestMethod]
        public void ThrowsFormatExceptionOnBrokenOvenProportionalControlUpdatesLine(string row)
        {
            Assert.ThrowsException<FormatException>(() => new IOconfOvenProportionalControlUpdates(row, 0));
        }
    }
}
