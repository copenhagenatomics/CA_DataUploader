using System;
using System.IO;
using System.IO.Pipelines;
using System.Text;
using System.Threading.Tasks;
using CA_DataUploaderLib;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace UnitTests
{
     [TestClass]
    public class ThirdPartyProtocolDetectionUnitTests
    {
        [TestMethod]
        public async Task DetectsLuminoxSensor()
        {
            var data = "O 0213.1 T +21.0 P 1019 % 020.92 e 0000\n";
            var dataBytes = Encoding.ASCII.GetBytes(data);
            using var ms = new MemoryStream(dataBytes);
            var reader = PipeReader.Create(ms);
            var detectedInfo = await MCUBoard.DetectThirdPartyProtocol(9600, "USB1-1.1", reader);
            Assert.IsNotNull(detectedInfo);
            Assert.AreEqual("Luminox O2", detectedInfo.ProductType);
        }

        [TestMethod]
        public async Task DetectsScaleSensor()
        {
            var data = "+0000.00 kg\n";
            var dataBytes = Encoding.ASCII.GetBytes(data);
            using var ms = new MemoryStream(dataBytes);
            var reader = PipeReader.Create(ms);
            var detectedInfo = await MCUBoard.DetectThirdPartyProtocol(9600, "USB1-1.1", reader);
            Assert.IsNotNull(detectedInfo);
            Assert.AreEqual("Scale", detectedInfo.ProductType);
        }

        [TestMethod]
        public async Task DetectZE03Sensor()
        {
            var dataBytes = Convert.FromHexString("FF8600D105010000A3");
            using var ms = new MemoryStream(dataBytes);
            var reader = PipeReader.Create(ms);
            var detectedInfo = await MCUBoard.DetectThirdPartyProtocol(9600, "USB1-1.1", reader);
            Assert.IsNotNull(detectedInfo);
            Assert.AreEqual("ze03", detectedInfo.ProductType);
        }
    }
}