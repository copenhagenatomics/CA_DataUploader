using CA_DataUploaderLib;
using CA_DataUploaderLib.Extensions;
using CA_DataUploaderLib.IOconf;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace UnitTests
{
    [TestClass]
    public class IOconfRedundantSensorsTests
    {
        [ClassInitialize]
        public static void ClassInitialize(TestContext _) => Redundancy.RegisterSystemExtensions(IOconfFileLoader.Loader);

        [TestMethod]
        public void ValidateDependencies_PointingToNonexistentSensor_Fail()
        {
            // Act
            var ex = Assert.ThrowsException<FormatException>(() => _ = new IOconfFile(@"
RedundantSensors; redundant; doesnotexist
".SplitNewLine(StringSplitOptions.None)));

            // Assert
            Assert.IsTrue(ex.Message.Contains("Failed to find"));
        }

        [TestMethod]
        public void ValidateDependencies_PointingToExistingSensor_Ok()
        {
            // Act
            _ = new IOconfFile(@"
Map; 4900553433511235353734; tm01
TypeJ; temperature_tm01_01; tm01; 1
RedundantSensors; redundant; temperature_tm01_01
".SplitNewLine(StringSplitOptions.None));
            
        }

        [TestMethod]
        public void ValidateDependencies_PointingToExpandedInputField_Ok()
        {
            // Act
            _ = new IOconfFile(@"
Map; 3900553433511235353736; dc01
SwitchboardSensor; switchboardSensor_dc01; dc01
RedundantSensors; switchboardSensor_redundant; switchboardSensor_dc01_rms
".SplitNewLine(StringSplitOptions.None));
        }

        [TestMethod]
        public void ValidateDependencies_PointingToBaseExpandableInput_Fail()
        {
            // Act
            var ex = Assert.ThrowsException<FormatException>(() => _ = new IOconfFile(@"
Map; 3900553433511235353736; dc01
SwitchboardSensor; switchboardSensor_dc01; dc01
RedundantSensors; switchboardSensor_redundant; switchboardSensor_dc01
".SplitNewLine(StringSplitOptions.None)));

            // Assert
            Assert.IsTrue(ex.Message.Contains("Failed to find"));
        }

        [TestMethod]
        public void ValidateDependencies_PointingToMath_Ok()
        {
            // Act
            _ = new IOconfFile(@"
Map; 3900553433511235353736; dc01
Math; math; math
RedundantSensors; redundant; math
".SplitNewLine(StringSplitOptions.None));
        }

        [TestMethod]
        public void ValidateDependencies_PointingToFilter_Ok()
        {
            // Act
            _ = new IOconfFile(@"
Map; 3900553433511235353736; dc01
Math; math; math
Filter; math; Min;600; math
RedundantSensors; redundant; math_filter
".SplitNewLine(StringSplitOptions.None));
        }

        [TestMethod]
        public void ValidateDependencies_PointingToHeaterDefinedAfter_Ok()
        {
            // Act
            _ = new IOconfFile(@"
Map; 3900553433511235353736; ac01
RedundantSensors; redundant; Heater01Top_current
Heater;Heater01Top;ac01;01;850
".SplitNewLine(StringSplitOptions.None));
        }

    }
}
