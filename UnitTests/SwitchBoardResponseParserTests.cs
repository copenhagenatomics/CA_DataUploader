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

        [TestMethod]
        public void OnlyNumbersWithoutStatesAndTemperaturesIsParsed()
        {
            string testString = "0.03,2.30,4.00,5.25";
            var values = IOconfOut230Vac.SwitchBoardResponseParser.Default.TryParseAsDoubleList(testString);
            Assert.IsNotNull(values);
            CollectionAssert.AreEqual(new [] {0.03d,2.30,4.00,5.25,10000,10000,10000,10000,10000}, values);
        }

        [TestMethod]
        public void OnlyNumbersWithoutStatesIsParsed()
        {
            string testString = "0.03,2.30,4.00,5.25,90";
            var values = IOconfOut230Vac.SwitchBoardResponseParser.Default.TryParseAsDoubleList(testString);
            Assert.IsNotNull(values);
            CollectionAssert.AreEqual(new [] {0.03d,2.30,4.00,5.25,10000,10000,10000,10000,90}, values);
        }

        [TestMethod]
        public void OnlyNumbersWithoutTemperatureIsParsed()
        {
            string testString = "0.03,2.30,4.00,5.25,1,0,1,1";
            var values = IOconfOut230Vac.SwitchBoardResponseParser.Default.TryParseAsDoubleList(testString);
            Assert.IsNotNull(values);
            CollectionAssert.AreEqual(new [] {0.03d,2.30,4.00,5.25,1,0,1,1,10000}, values);
        }

        [TestMethod]
        public void OnlyNumbersAllValuesIsParsed()
        {
            string testString = "0.03,2.30,4.00,5.25,1,0,1,1,40";
            var values = IOconfOut230Vac.SwitchBoardResponseParser.Default.TryParseAsDoubleList(testString);
            Assert.IsNotNull(values);
            CollectionAssert.AreEqual(new [] {0.03d,2.30,4.00,5.25,1,0,1,1,40}, values);
        }
        
        [TestMethod]
        public void InvalidLineIsRejected()
        {
            string testString = "MISREAD: something went wrong";
            var values = IOconfOut230Vac.SwitchBoardResponseParser.Default.TryParseAsDoubleList(testString);
            Assert.IsNull(values);
        }

        [TestMethod]
        public void LineConfirmationIsRejected()
        {
            string testString = "p1 on 60";
            var values = IOconfOut230Vac.SwitchBoardResponseParser.Default.TryParseAsDoubleList(testString);
            Assert.IsNull(values);
        }

        [TestMethod]
        public void LineConfirmationIsRecognizedAsExpectedNonValuesLine()
        {
            string testString = "p1 on 60";
            var value = IOconfOut230Vac.SwitchBoardResponseParser.Default.IsExpectedNonValuesLine(testString);
            Assert.IsTrue(value);
        }
    }
}
