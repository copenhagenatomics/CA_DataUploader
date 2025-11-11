using CA.LoopControlPluginBase;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace UnitTests
{
    [TestClass]
    public class DataVectorTests
    {
        [TestMethod]
        public void TimeAfterWith600HoursDoesNotOverflow()
        {
            // Arrange
            var baseTime = new DateTime(2020, 1, 1, 0, 0, 0);
            var data = new double[10];
            var vector = new DataVector(baseTime, data);
            var milliseconds600Hours = 600L * 60 * 60 * 1000; // exceeds int.MaxValue (2,147,483,647)

            // Act
            var result = vector.TimeAfter(milliseconds600Hours);

            // Assert
            var expectedTime = baseTime.AddMilliseconds(milliseconds600Hours);
            var expectedOADate = expectedTime.ToOADate();
            Assert.AreEqual(expectedOADate, result, 0.0001);
        }

        [TestMethod]
        public void TimeAfterWithIntMaxValueWorks()
        {
            // Arrange
            var baseTime = new DateTime(2020, 1, 1, 0, 0, 0);
            var data = new double[10];
            var vector = new DataVector(baseTime, data);

            // Act - tests the old int overload still works (backward compatibility)
            var result = vector.TimeAfter(int.MaxValue);

            // Assert
            var expectedTime = baseTime.AddMilliseconds(int.MaxValue);
            var expectedOADate = expectedTime.ToOADate();
            Assert.AreEqual(expectedOADate, result, 0.0001);
        }

        [TestMethod]
        public void TimeAfterWithOneYearWorks()
        {
            // Arrange
            var baseTime = new DateTime(2020, 1, 1, 0, 0, 0);
            var data = new double[10];
            var vector = new DataVector(baseTime, data);
            var oneYearInMs = 365L * 24 * 60 * 60 * 1000;

            // Act
            var result = vector.TimeAfter(oneYearInMs);

            // Assert
            var expectedTime = baseTime.AddMilliseconds(oneYearInMs);
            var expectedOADate = expectedTime.ToOADate();
            Assert.AreEqual(expectedOADate, result, 0.0001);
        }
    }
}

