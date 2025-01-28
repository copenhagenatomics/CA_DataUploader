using Microsoft.VisualStudio.TestTools.UnitTesting;
using CA_DataUploaderLib.Extensions;
using CA_DataUploaderLib.IOconf;
using System;
using System.Linq;

namespace UnitTests
{
    [TestClass]
    public class IOconfFileTests
    {
        [TestMethod]
        public void WhenTwoRowsInDifferentGroupsHaveTheSameName_ThenNoExceptionIsThrown()
        {
            var _ = new IOconfFile(new(){
                "Map; 1234567890; tm01",
                "TypeK; sameName; tm01; 1",
                "TypeJ; sameName; tm01; 2" });
        }

        [TestMethod]
        [ExpectedException(typeof(Exception), "An exception should have been thrown but was not.")]
        public void WhenTwoRowsInTheSameGroupHaveTheSameName_ThenAnExceptionIsThrown()
        {
            var _ = new IOconfFile(new(){
                "Map; 1234567890; tm01",
                "TypeJ; sameName; tm01; 1",
                "TypeJ; sameName; tm01; 2" });
        }

        [TestMethod]
        public void GetBoardStateNames_TemperatureField()
        {
            // Arrange
            var ioconf = new IOconfFile(@$"
Map; 4900553433511235353734; tm01
TypeJ; temperature_tm01_01; tm01; 1
".SplitNewLine(StringSplitOptions.None));

            // Act
            var boardStateNames = ioconf.GetBoardStateNames("temperature_tm01_01").ToList();

            // Assert
            Assert.AreEqual(1, boardStateNames.Count());
            Assert.AreEqual("tm01_state", boardStateNames.First());
        }

        [TestMethod]
        public void GetBoardStateNames_TemperatureField_Filter()
        {
            // Arrange
            var ioconf = new IOconfFile(@"
Map; 4900553433511235353734; tm01
TypeJ; temperature_tm01_01; tm01; 1
Filter;temperature_tm01_01; Min;600;temperature_tm01_01
".SplitNewLine(StringSplitOptions.None));

            // Act
            var boardStateNames = ioconf.GetBoardStateNames("temperature_tm01_01_filter").ToList();

            // Assert
            Assert.AreEqual(1, boardStateNames.Count());
            Assert.AreEqual("tm01_state", boardStateNames.First());
        }

        [TestMethod]
        public void GetBoardStateNames_TemperatureField_Math()
        {
            // Arrange
            var ioconf = new IOconfFile(@"
Map; 4900553433511235353734; tm01
TypeJ; temperature_tm01_01; tm01; 1
Math; temperature_tm01_01_math; temperature_tm01_01 + 1
".SplitNewLine(StringSplitOptions.None));

            // Act
            var boardStateNames = ioconf.GetBoardStateNames("temperature_tm01_01_math").ToList();

            // Assert
            Assert.AreEqual(1, boardStateNames.Count());
            Assert.AreEqual("tm01_state", boardStateNames.First());
        }
    }
}
