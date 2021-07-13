using Microsoft.VisualStudio.TestTools.UnitTesting;
using CA_DataUploaderLib.IOconf;

namespace UnitTests
{
    [TestClass]
    public class DefaultResponseParserTests
    {
        [TestMethod]
        public void PressureLineIsParsed()
        {
            string testString = "0.00, 0.05, -1.80, -1.80, -1.80, -1.78\r";
            var values = BoardSettings.Default.Parser.TryParseAsDoubleList(testString);
            Assert.IsNotNull(values);
            CollectionAssert.AreEqual(new [] {0d, 0.05, -1.8, -1.8, -1.8, -1.78}, values);
        }
    }
}
