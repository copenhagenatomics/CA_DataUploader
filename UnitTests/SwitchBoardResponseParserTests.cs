using Microsoft.VisualStudio.TestTools.UnitTesting;
using CA_DataUploaderLib.IOconf;

namespace UnitTests
{
    [TestClass]
    public class SwitchBoardResponseParserTests
    {
        [TestMethod]
        public void SingleLineWithoutStatesIsParsed()
        {
            string testString = "asdfP1=0.00A P2=0.00A P3=0.00A P4=0.00A ";
            var values = IOconfOut230Vac.SwitchBoardResponseParser.Default.TryParseAsDoubleList(testString);
            Assert.IsNotNull(values);
            CollectionAssert.AreEqual(new [] {0d, 0, 0, 0, 10000, 10000, 10000, 10000, 10000}, values);
        }

        [TestMethod]
        public void TwoLinesWithoutStatesIsParsed()
        {
            string testString = "asdfP1=3.20A P2=2.30A P3=3.40A P4=5.60A \rasdfP1=3.20A P2=2.30A P3=3.40A P4=5.60A ";
            var values = IOconfOut230Vac.SwitchBoardResponseParser.Default.TryParseAsDoubleList(testString);
            Assert.IsNotNull(values);
            CollectionAssert.AreEqual(new [] {3.2d, 2.3d, 3.4, 5.6, 10000, 10000, 10000, 10000, 10000}, values);
        }

        [TestMethod]
        public void LineWithStatesIsParsed()
        {
            string testString = "ssP1=0.06A P2=0.05A P3=0.05A P4=0.06A 0, 1, 0, 1, 25.87 aa";
            var values = IOconfOut230Vac.SwitchBoardResponseParser.Default.TryParseAsDoubleList(testString);
            Assert.IsNotNull(values);
            CollectionAssert.AreEqual(new [] {0.06d, 0.05d, 0.05d, 0.06d, 0, 1, 0, 1, 25.87d}, values);
        }
    }
}
