using Microsoft.VisualStudio.TestTools.UnitTesting;
using CA_DataUploaderLib.IOconf;

namespace UnitTests
{
    [TestClass]
    public class SwitchBoardResponseParserTests
    {
        [TestMethod]
        public void OnlyNumbersWithoutTemperatureOrSensorPort() 
        {
            string testString = "0.03,2.30,4.00,5.25";
            var (values, _) = IOconfOut230Vac.SwitchBoardResponseParser.Default.TryParseAsDoubleList(testString);
            Assert.IsNotNull(values);
            CollectionAssert.AreEqual(new [] {0.03d,2.30,4.00,5.25,10000}, values); //temperature is not optional as it requires discovery during init sequence which we don't support
        }

        [TestMethod]
        public void OnlyNumbersWithoutSensorPort()
        {
            string testString = "0.03,2.30,4.00,5.25,90";
            var (values, _) = IOconfOut230Vac.SwitchBoardResponseParser.Default.TryParseAsDoubleList(testString);
            Assert.IsNotNull(values);
            CollectionAssert.AreEqual(new [] {0.03d,2.30,4.00,5.25,90}, values);
        }

        [TestMethod]
        public void OnlyNumbersAllValuesIsParsed()
        {
            string testString = "0.03,2.30,4.00,5.25,40,150.5,1045";
            var (values, status) = IOconfOut230Vac.SwitchBoardResponseParser.Default.TryParseAsDoubleList(testString);
            Assert.IsNotNull(values);
            CollectionAssert.AreEqual(new [] {0.03d,2.30,4.00,5.25,40,150.5,1045}, values);
            Assert.AreEqual(0U, status);
        }

        [TestMethod]
        public void OnlyNumbersAllValuesAndStatusIsParsed()
        {
            string testString = "0.03,2.30,4.00,5.25,40,150.5,1045,0xff";
            var (values, status) = IOconfOut230Vac.SwitchBoardResponseParser.Default.TryParseAsDoubleList(testString);
            Assert.IsNotNull(values);
            CollectionAssert.AreEqual(new[] { 0.03d, 2.30, 4.00, 5.25, 40, 150.5, 1045 }, values);
            Assert.AreEqual(255U, status);
        }

        [TestMethod]
        public void InvalidLineIsRejected()
        {
            string testString = "MISREAD: something went wrong";
            var (values, status) = IOconfOut230Vac.SwitchBoardResponseParser.Default.TryParseAsDoubleList(testString);
            Assert.IsNull(values);
            Assert.AreEqual(0U, status);
        }

        [TestMethod]
        public void InvalidLineWithOnlyProperCommandIsRejected()
        {
            string testString = "MISREAD: p1 on 60";
            var (values, _) = IOconfOut230Vac.SwitchBoardResponseParser.Default.TryParseAsDoubleList(testString);
            Assert.IsNull(values);
        }

        [TestMethod]
        public void InvalidLineIncludingProperCommandIsRejected()
        {
            string testString = "MISREAD: something p1 on 60";
            var (values, _) = IOconfOut230Vac.SwitchBoardResponseParser.Default.TryParseAsDoubleList(testString);
            Assert.IsNull(values);
        }

        [TestMethod]
        public void InvalidLineIncludingProperCommandAndBoardValuesLineIsRejected()
        {
            string testString = "MISREAD: 0.03,2.30,4.00,5.25,40p1 on 60";
            var (values, _) = IOconfOut230Vac.SwitchBoardResponseParser.Default.TryParseAsDoubleList(testString);
            Assert.IsNull(values);
        }

        [TestMethod]
        public void LineConfirmationIsRejected()
        {
            string testString = "p1 on 60";
            var (values, _) = IOconfOut230Vac.SwitchBoardResponseParser.Default.TryParseAsDoubleList(testString);
            Assert.IsNull(values);
        }

        [TestMethod]
        public void MisreadIsNotRecognizedAsExpectedNonValuesLine()
        {
            string testString = "MISREAD: random data";
            var value = IOconfOut230Vac.SwitchBoardResponseParser.Default.IsExpectedNonValuesLine(testString);
            Assert.IsFalse(value);
        }

        [TestMethod]
        public void MisreadWithOnlyProperCommandIsNotRecognizedAsExpectedNonValuesLine()
        {
            string testString = "MISREAD: psssss1 on 60";
            var value = IOconfOut230Vac.SwitchBoardResponseParser.Default.IsExpectedNonValuesLine(testString);
            Assert.IsFalse(value);
        }

        [TestMethod]
        public void MisreadIncludingProperCommandIsNotRecognizedAsExpectedNonValuesLine()
        {
            string testString = "MISREAD: something p1 on 60";
            var value = IOconfOut230Vac.SwitchBoardResponseParser.Default.IsExpectedNonValuesLine(testString);
            Assert.IsFalse(value);
        }

        [TestMethod]
        public void MisreadIncludingProperCommandAndValuesLineIsNotRecognizedAsExpectedNonValuesLine()
        {
            string testString = "MISREAD: 0.03,2.30,4.00,5.25,40p1 on 60";
            var value = IOconfOut230Vac.SwitchBoardResponseParser.Default.IsExpectedNonValuesLine(testString);
            Assert.IsFalse(value);
        }
    }
}
