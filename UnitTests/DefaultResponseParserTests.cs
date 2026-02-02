using Microsoft.VisualStudio.TestTools.UnitTesting;
using CA_DataUploaderLib.IOconf;

namespace UnitTests
{
    [TestClass]
    public class DefaultResponseParserTests
    {
        [DataRow("0.01,-0.05,0.06\r", 0U, 0.01, -0.05, 0.06)]
        [DataRow("-0.01,0.05,0.06,", 0U, -0.01, 0.05, 0.06)]
        [DataRow("0.01, -0.05, 0.06", 0U, 0.01, -0.05, 0.06)]
        [DataRow("+0.01, +0.05, +0.06", 0U, 0.01, 0.05, 0.06)]
        [DataRow("0.01,-0.05,0.06,0x0", 0U, 0.01, -0.05, 0.06)]
        [DataRow("0.01,-0.05,0.06,0xa ", 10U, 0.01, -0.05, 0.06)]
        [DataRow("0.01,-0.05,0.06,0xa,", 10U, 0.01, -0.05, 0.06)] //Comma after status supported, but not expected
        [DataRow("0.01,-0.05,0.06, 0xA", 10U, 0.01, -0.05, 0.06)]
        [DataRow("0.01,-0.05,0.06,0xffffffff", uint.MaxValue, 0.01, -0.05, 0.06)]
        [TestMethod]
        public void DefaultSettingsParsesExpectedValuesFormat(string line, uint expectedStatus, params double[] expectedValues)
        {
            var parser = BoardSettings.Default.Parser;
            var (values, status) = parser.TryParseAsDoubleList(line);
            CollectionAssert.AreEqual(expectedValues, values);
            Assert.AreEqual(expectedStatus, status);
        }
    }
}
