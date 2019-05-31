using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CA_DataUploaderLib;

namespace UnitTests
{
    [TestClass]
    public class CALogTests
    {
        [TestMethod]
        public void TestMethod1()
        {
            CALog.LogInfoAndConsoleLn(LogID.B, "Thorium salt energy is gooood");
        }
    }
}
