using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CA_DataUploaderLib.Extensions;

namespace UnitTests
{
    [TestClass]
    public class HeatingControllerTests
    {
        private const string _SwitchBoxPattern = "P1=(\\d\\.\\d\\d)A; P2=(\\d\\.\\d\\d)A; P3=(\\d\\.\\d\\d)A; P4=(\\d\\.\\d\\d)A;";

        [TestMethod]
        public void TestMethod1()
        {
            string testString = "asdfP1=0.00A; P2=0.00A; P3=0.00A; P4=0.00A; ";
            var match = Regex.Match(testString, _SwitchBoxPattern);
            if (match.Success)
            {
                Debug.Print(match.Groups.Count.ToString());
            }
        }

        [TestMethod]
        public void TestMethod2()
        {
            string testString = "asdfP1=3.20A; P2=2.30A; P3=3.40A; P4=5.60A; \rasdfP1=3.20A; P2=2.30A; P3=3.40A; P4=5.60A; ";
            var match = Regex.Match(testString, _SwitchBoxPattern);
            if (match.Success)
            {
                double dummy;
                var values = match.Groups.Cast<Group>().Skip(1)
                    .Where(x => x.Value.TryToDouble(out dummy))
                    .Select(x => x.Value.ToDouble()).ToArray();
                Debug.Print(values.Count().ToString());
            }
        }
    }
}
