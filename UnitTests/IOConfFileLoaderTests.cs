using Microsoft.VisualStudio.TestTools.UnitTesting;
using CA_DataUploaderLib.IOconf;
using System.Linq;
using System.Collections.Generic;

namespace UnitTests
{
    [TestClass]
    public class IOconfFileLoaderTests
    {
        private static IEnumerable<IOconfRow> ParseLines(string[] lines) => IOconfFileLoader.ParseLines(IOconfFileLoader.Loader, lines);

        [TestMethod]
        public void CanLoadAccountLine()
        {
            var rowsEnum = ParseLines(["Account;john;john.doe@example.com;johndoepass"]);
            var rows = rowsEnum.ToArray();
            Assert.AreEqual(1, rows.Length);
            Assert.IsInstanceOfType(rows[0], typeof(IOconfAccount));
            var account = (IOconfAccount)rows[0];
            Assert.AreEqual("john-john.doe@example.com-johndoepass", $"{account.Name}-{account.Email}-{account.Password}");
        }

        [TestMethod]
        public void CanLoadMathLine()
        {
            var rowsEnum = ParseLines(["Math;mymath;heater1 + 5"]);
            var rows = rowsEnum.ToArray();
            Assert.AreEqual(1, rows.Length);
            Assert.IsInstanceOfType(rows[0], typeof(IOconfMath));
            var math = (IOconfMath)rows[0];
            Assert.AreEqual("mymath", math.Name);
            Assert.AreEqual(405, math.Calculate(new() { {"heater1", 400} }));
        }

        [TestMethod]
        public void CanLoadGenericOutputLine()
        {
            var rowsEnum = ParseLines(["GenericOutput;generic_ac_on;realacbox2;0;p1 $heater1_onoff 3"]);
            var rows = rowsEnum.ToArray();
            Assert.AreEqual(1, rows.Length);
            Assert.IsInstanceOfType(rows[0], typeof(IOconfGenericOutput));
            var output = (IOconfGenericOutput)rows[0];
            Assert.AreEqual("generic_ac_on", output.Name);
            Assert.AreEqual(0, output.DefaultValue);
            Assert.AreEqual("heater1_onoff", output.TargetField);
            Assert.AreEqual("p1 5 3", output.GetCommand(5));
        }

        [TestMethod]
        public void CanLoadGenericOutputLineWithBraces()
        {
            var rowsEnum = ParseLines(["GenericOutput;generic_ac_on;realacbox2;0;p1 on 3 ${heater1_onoff}00%"]);
            var rows = rowsEnum.ToArray();
            Assert.AreEqual(1, rows.Length);
            Assert.IsInstanceOfType(rows[0], typeof(IOconfGenericOutput));
            var output = (IOconfGenericOutput)rows[0];
            Assert.AreEqual("generic_ac_on", output.Name);
            Assert.AreEqual(0, output.DefaultValue);
            Assert.AreEqual("heater1_onoff", output.TargetField);
            Assert.AreEqual("p1 on 3 100%", output.GetCommand(1));
        }

        [TestMethod]
        public void CanLoadCustomConfigWithoutMixingPrefix()
        {
            IOconfFileLoader.Loader.AddLoader("Mathing", (row, lineIndex) => new IOConfMathing(row, lineIndex));
            var rowsEnum = ParseLines(["Mathing;mymath;heater1 + 5"]);
            var rows = rowsEnum.ToArray();
            Assert.AreEqual(1, rows.Length);
            Assert.IsInstanceOfType(rows[0], typeof(IOConfMathing));
        }

        [TestMethod]
        public void CanLoadCurrentLine()
        {
            var rowsEnum = ParseLines(["Current;current_ct01;ct01;2;300"]);
            var rows = rowsEnum.ToArray();
            Assert.AreEqual(1, rows.Length);
            Assert.IsInstanceOfType(rows[0], typeof(IOconfCurrent));
            var current = (IOconfCurrent)rows[0];
            Assert.AreEqual("current_ct01", current.Name);
            Assert.AreEqual(2, current.PortNumber);
        }

        private class IOConfMathing : IOconfRow
        {
            public IOConfMathing(string row, int lineIndex) : base(row, lineIndex, "Mathing") {}
        }
    }
}
