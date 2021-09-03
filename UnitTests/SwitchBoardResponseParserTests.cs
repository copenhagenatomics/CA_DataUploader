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
            CollectionAssert.AreEqual(new [] {0d, 0, 0, 0, 10000}, values);
        }

        [TestMethod]
        public void TwoLinesWithoutStatesIsParsed()
        {
            string testString = "asdfP1=3.20A P2=2.30A P3=3.40A P4=5.60A \rasdfP1=3.20A P2=2.30A P3=3.40A P4=5.60A ";
            var values = IOconfOut230Vac.SwitchBoardResponseParser.Default.TryParseAsDoubleList(testString);
            Assert.IsNotNull(values);
            CollectionAssert.AreEqual(new [] {3.2d, 2.3d, 3.4, 5.6, 10000}, values);
        }

        [TestMethod]
        public void LineWithStatesIsParsed()
        {
            string testString = "ssP1=0.06A P2=0.05A P3=0.05A P4=0.06A 0, 1, 0, 1, 25.87 aa";
            var values = IOconfOut230Vac.SwitchBoardResponseParser.Default.TryParseAsDoubleList(testString);
            Assert.IsNotNull(values);
            CollectionAssert.AreEqual(new [] {0.06d, 0.05d, 0.05d, 0.06d, 25.87d}, values);
        }

        [TestMethod]
        public void OnlyNumbersWithoutTemperatureOrSensorPort() 
        {
            string testString = "0.03,2.30,4.00,5.25";
            var values = IOconfOut230Vac.SwitchBoardResponseParser.Default.TryParseAsDoubleList(testString);
            Assert.IsNotNull(values);
            CollectionAssert.AreEqual(new [] {0.03d,2.30,4.00,5.25,10000}, values); //temperature is not optional as it requires discovery during init sequence which we don't support
        }

        [TestMethod]
        public void OnlyNumbersWithoutSensorPort()
        {
            string testString = "0.03,2.30,4.00,5.25,90";
            var values = IOconfOut230Vac.SwitchBoardResponseParser.Default.TryParseAsDoubleList(testString);
            Assert.IsNotNull(values);
            CollectionAssert.AreEqual(new [] {0.03d,2.30,4.00,5.25,90}, values);
        }

        [TestMethod]
        public void OnlyNumbersAllValuesIsParsed()
        {
            string testString = "0.03,2.30,4.00,5.25,40,150.5,1045";
            var values = IOconfOut230Vac.SwitchBoardResponseParser.Default.TryParseAsDoubleList(testString);
            Assert.IsNotNull(values);
            CollectionAssert.AreEqual(new [] {0.03d,2.30,4.00,5.25,40,150.5,1045}, values);
        }
        
        [TestMethod]
        public void InvalidLineIsRejected()
        {
            string testString = "MISREAD: something went wrong";
            var values = IOconfOut230Vac.SwitchBoardResponseParser.Default.TryParseAsDoubleList(testString);
            Assert.IsNull(values);
        }

        [TestMethod]
        public void InvalidLineWithOnlyProperCommandIsRejected()
        {
            string testString = "MISREAD: p1 on 60";
            var values = IOconfOut230Vac.SwitchBoardResponseParser.Default.TryParseAsDoubleList(testString);
            Assert.IsNull(values);
        }

        [TestMethod]
        public void InvalidLineIncludingProperCommandIsRejected()
        {
            string testString = "MISREAD: something p1 on 60";
            var values = IOconfOut230Vac.SwitchBoardResponseParser.Default.TryParseAsDoubleList(testString);
            Assert.IsNull(values);
        }

        [TestMethod]
        public void InvalidLineIncludingProperCommandAndBoardValuesLineIsRejected()
        {
            string testString = "MISREAD: 0.03,2.30,4.00,5.25,40p1 on 60";
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
