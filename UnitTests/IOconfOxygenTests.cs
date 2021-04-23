using CA_DataUploaderLib.IOconf;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace UnitTests
{
    [TestClass]
    public class IOconfOxyenTests
    {
        [TestMethod]
        public void AlertTriggers() 
        {
            string input = "O 0213.1 T +21.1 P 1019 % 020.92 e 0001";
            var parser = IOconfOxygen.LineParser.Default;
            var output = parser.TryParseAsDoubleList(input);
            CollectionAssert.AreEqual(new []{213.1, 21.1, 0.019, 20.92, 1}, output);
        }
    }
}
