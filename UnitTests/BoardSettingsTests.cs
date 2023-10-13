using Microsoft.VisualStudio.TestTools.UnitTesting;
using CA_DataUploaderLib.IOconf;

namespace UnitTests
{
    [TestClass]
    public class BoardSettingsTests
    {
        [TestMethod]
        public void CanSetCalibrationAtIndex()
        {
            var settings = new BoardSettings();
            settings.SetCalibrationAtIndex("SSSSSSF", 'G', 1);
            settings.SetCalibrationAtIndex("SSSSSSF", 'M', 3);
            Assert.AreEqual("SGSMSSF", settings.Calibration);
        }
    }
}
