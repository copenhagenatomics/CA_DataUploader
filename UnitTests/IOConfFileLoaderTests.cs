using Microsoft.VisualStudio.TestTools.UnitTesting;
using CA_DataUploaderLib.IOconf;
using System.Linq;

namespace UnitTests
{
    [TestClass]
    public class IOConfFileLoaderTests
    {
        [TestMethod]
        public void CanLoadAccountLine()
        {
            var rowsEnum = IOconfFileLoader.ParseLines(new[] { "Account;john;john.doe@example.com;johndoepass" });
            var rows = rowsEnum.ToArray();
            Assert.AreEqual(1, rows.Length);
            Assert.IsInstanceOfType(rows[0], typeof(IOconfAccount));
            var account = (IOconfAccount)rows[0];
            Assert.AreEqual("john-john.doe@example.com-johndoepass", $"{account.Name}-{account.Email}-{account.Password}");
        }

        [TestMethod]
        public void CanLoadMathLine()
        {
            var rowsEnum = IOconfFileLoader.ParseLines(new[] { "Math;mymath;heater1 + 5" });
            var rows = rowsEnum.ToArray();
            Assert.AreEqual(1, rows.Length);
            Assert.IsInstanceOfType(rows[0], typeof(IOconfMath));
            var math = (IOconfMath)rows[0];
            Assert.AreEqual("mymath", math.Name);
            Assert.AreEqual(405, math.Calculate(new() { {"heater1", 400} }).Value);
        }


        [TestMethod]
        public void CanLoadCustomConfigWithoutMixingPrefix()
        {
            IOconfFileLoader.AddLoader("Mathing", (row, lineIndex) => new IOConfMathing(row, lineIndex));
            var rowsEnum = IOconfFileLoader.ParseLines(new[] { "Mathing;mymath;heater1 + 5" });
            var rows = rowsEnum.ToArray();
            Assert.AreEqual(1, rows.Length);
            Assert.IsInstanceOfType(rows[0], typeof(IOConfMathing));
        }

        private class IOConfMathing : IOconfRow
        {
            public IOConfMathing(string row, int lineIndex) : base(row, lineIndex, "Mathing") {}
        }
    }
}
