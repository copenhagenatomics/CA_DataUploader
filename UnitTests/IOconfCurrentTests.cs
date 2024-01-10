﻿using CA_DataUploaderLib;
using CA_DataUploaderLib.IOconf;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Globalization;

namespace UnitTests
{
    [TestClass]
    public class IOconfCurrentTests
    {
        private readonly string boxName = "currentBoard12345";
        private readonly string portName = RpiVersion.IsWindows() ? "COM3" : "USB1-2-3";
        private readonly string portPrefix = RpiVersion.IsWindows() ? "" : "/dev/";

        [TestMethod]
        public void BoardWithoutCalibrationDoesNotGetUpdated()
        {
            // Arrange
            IOconfMap? mapLine = null;
            using var _ = TestableIOconfFile.Override(new() { GetMap = () => new[] { mapLine = new IOconfMap($"Map; {portName}; {boxName}", 0) } });
            var loadSideRating = 300;
            string? boardCalibration = null;

            // Act
            var ioConf = new IOconfCurrent($"Current; myCurrent; {boxName}; 2; {loadSideRating.ToString(CultureInfo.InvariantCulture)}", 0);
            mapLine!.Setboard(new TestBoard(portPrefix + portName, mapLine, boardCalibration));

            // Assert
            Assert.IsNull(mapLine!.BoardSettings.Calibration);
        }


        [TestMethod]
        public void BoardNotSupportingCalibrationDoesNotGetUpdated()
        {
            // Arrange
            IOconfMap? mapLine = null;
            using var _ = TestableIOconfFile.Override(new() { GetMap = () => new[] { mapLine = new IOconfMap($"Map; {portName}; {boxName}", 0) } });
            var loadSideRating = 300;
            string? boardCalibration = "Old";

            // Act
            var ioConf = new IOconfCurrent($"Current; myCurrent; {boxName}; 2; {loadSideRating.ToString(CultureInfo.InvariantCulture)}", 0);
            mapLine!.Setboard(new TestBoard(portPrefix + portName, mapLine, boardCalibration));

            // Assert
            Assert.IsNull(mapLine!.BoardSettings.Calibration);
        }

        [TestMethod]
        public void BoardCalibrationGetsUpdated()
        {
            // Arrange
            IOconfMap? mapLine = null;
            using var _ = TestableIOconfFile.Override(new() { GetMap = () => new[] { mapLine = new IOconfMap($"Map; {portName}; {boxName}", 0) } });
            var loadSideRating = 150;
            string? boardCalibration = "CAL 1,60.000000,0 2,60.000000,0 3,60.000000,0";

            // Act
            var ioConf = new IOconfCurrent($"Current; myCurrent; {boxName}; 2; {loadSideRating.ToString(CultureInfo.InvariantCulture)}", 0);
            mapLine!.Setboard(new TestBoard(portPrefix + portName, mapLine, boardCalibration));

            // Assert
            var decimalDigits = new NumberFormatInfo() { NumberDecimalDigits = 6 };
            var expectedScalar = (loadSideRating / 5).ToString("F", decimalDigits);
            var expectedBoardCalibration = $"CAL 1,60.000000,0 2,{expectedScalar},0 3,60.000000,0";
            Assert.AreEqual(expectedBoardCalibration, mapLine!.BoardSettings.Calibration);
        }

        [TestMethod]
        public void BoardCalibrationWithCustomMeterSideRatingGetsUpdated()
        {
            // Arrange
            IOconfMap? mapLine = null;
            using var _ = TestableIOconfFile.Override(new() { GetMap = () => new[] { mapLine = new IOconfMap($"Map; {portName}; {boxName}", 0) } });
            var loadSideRating = 150;
            var meterSideRating = 2;
            string? boardCalibration = "CAL 1,60.000000,0 2,60.000000,0 3,60.000000,0";

            // Act
            var ioConf = new IOconfCurrent($"Current; myCurrent; {boxName}; 2; {loadSideRating.ToString(CultureInfo.InvariantCulture)}; {meterSideRating.ToString(CultureInfo.InvariantCulture)}", 0);
            mapLine!.Setboard(new TestBoard(portPrefix + portName, mapLine, boardCalibration));

            // Assert
            var decimalDigits = new NumberFormatInfo() { NumberDecimalDigits = 6 };
            var expectedScalar = (loadSideRating / meterSideRating).ToString("F", decimalDigits);
            var expectedBoardCalibration = $"CAL 1,60.000000,0 2,{expectedScalar},0 3,60.000000,0";
            Assert.AreEqual(expectedBoardCalibration, mapLine!.BoardSettings.Calibration);
        }

        [TestMethod]
        public void LoadSideRatingHasToBeSpecified()
        {
            // Arrange
            using var _ = TestableIOconfFile.Override(new() { GetMap = () => new[] { new IOconfMap($"Map; {portName}; {boxName}", 0) } });

            // Act + Assert
            var ex = Assert.ThrowsException<FormatException>(() => new IOconfCurrent($"Current; myCurrent; {boxName}; 2;  // <- no value here", 0));
            Assert.IsTrue(ex.Message.Contains("wrong format"), "missing expected part of the exception message");
        }

        [DataRow("-1",    false)]
        [DataRow("0",     true)]
        [DataRow("0.0",   true)]
        [DataRow("0,1",   false)]
        [DataRow("0.1",   true)]
        [DataRow("300",   true)]
        [DataRow("300.0", true)]
        [DataRow("300A",  false)]
        [DataRow("12345", true)]
        [DataTestMethod]
        public void LoadSideRatingHasToBeAValidNumber(string input, bool valid)
        {
            // Arrange
            using var _ = TestableIOconfFile.Override(new() { GetMap = () => new[] { new IOconfMap($"Map; {portName}; {boxName}", 0) } });

            // Act + Assert
            if (valid)
            {
                new IOconfCurrent($"Current; myCurrent; {boxName}; 2; {input}", 0);
            }
            else
            {
                var ex = Assert.ThrowsException<FormatException>(() => new IOconfCurrent($"Current; myCurrent; {boxName}; 2; {input}", 0));
                Assert.IsTrue(ex.Message.StartsWith("Unsupported load side rating"), "missing expected part of the exception message");
            }
        }

        [DataRow("",     true)]
        [DataRow("-1",   false)]
        [DataRow("0",    false)]
        [DataRow("0.0",  false)]
        [DataRow("1A",   false)]
        [DataRow("0,1",  false)]
        [DataRow("0.1",  true)]
        [DataRow("5.0",  true)]
        [DataRow("5",    true)]
        [DataRow("5.01", false)]
        [DataRow("12345",false)]
        [DataTestMethod]
        public void MeterSideRatingHasToBeAValidNumberWhenSpecified(string input, bool valid)
        {
            // Arrange
            using var _ = TestableIOconfFile.Override(new() { GetMap = () => new[] { new IOconfMap($"Map; {portName}; {boxName}", 0) } });

            // Act + Assert
            if (valid)
            {
                new IOconfCurrent($"Current; myCurrent; {boxName}; 2; 300; {input}", 0);
            }
            else
            {
                var ex = Assert.ThrowsException<FormatException>(() => new IOconfCurrent($"Current; myCurrent; {boxName}; 2; 300; {input}", 0));
                Assert.IsTrue(ex.Message.StartsWith("Unsupported meter side rating"), "missing expected part of the exception message");
            }
        }


        private class TestBoard : Board
        {
            public TestBoard(string portname, IOconfMap? map, string? calibration = null, string? productType = null) : base(portname, map, productType)
            {
                Calibration = calibration;
            }
        }
    }
}