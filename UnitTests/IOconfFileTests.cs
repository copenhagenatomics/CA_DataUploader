using Microsoft.VisualStudio.TestTools.UnitTesting;
using CA_DataUploaderLib.Extensions;
using CA_DataUploaderLib.IOconf;
using System;
using System.Linq;
using System.Linq.Expressions;

namespace UnitTests
{
    [TestClass]
    public class IOconfFileTests
    {
        [TestMethod]
        public void WhenTwoRowsInDifferentGroupsHaveTheSameName_ThenNoExceptionIsThrown()
        {
            var _ = new IOconfFile(new(){
                "Map; 1234567890; tm01",
                "TypeK; sameName; tm01; 1",
                "TypeJ; sameName; tm01; 2" });
        }

        [TestMethod]
        [ExpectedException(typeof(Exception), "An exception should have been thrown but was not.")]
        public void WhenTwoRowsInTheSameGroupHaveTheSameName_ThenAnExceptionIsThrown()
        {
            var _ = new IOconfFile(new(){
                "Map; 1234567890; tm01",
                "TypeJ; sameName; tm01; 1",
                "TypeJ; sameName; tm01; 2" });
        }

        [TestMethod]
        public void GetBoardStateNames_TemperatureField()
        {
            // Arrange
            var ioconf = new IOconfFile(@$"
Map; 4900553433511235353734; tm01
TypeJ; temperature_tm01_01; tm01; 1
".SplitNewLine(StringSplitOptions.None));

            // Act
            var boardStateNames = ioconf.GetBoardStateNames("temperature_tm01_01").ToList();

            // Assert
            Assert.AreEqual(1, boardStateNames.Count());
            Assert.AreEqual("tm01_state", boardStateNames.First());
        }

        [TestMethod]
        public void GetBoardStateNames_TemperatureField_Filter()
        {
            // Arrange
            var ioconf = new IOconfFile(@"
Map; 4900553433511235353734; tm01
TypeJ; temperature_tm01_01; tm01; 1
Filter;temperature_tm01_01; Min;600;temperature_tm01_01
".SplitNewLine(StringSplitOptions.None));

            // Act
            var boardStateNames = ioconf.GetBoardStateNames("temperature_tm01_01_filter").ToList();

            // Assert
            Assert.AreEqual(1, boardStateNames.Count());
            Assert.AreEqual("tm01_state", boardStateNames.First());
        }

        [TestMethod]
        public void GetBoardStateNames_TemperatureField_Math()
        {
            // Arrange
            var ioconf = new IOconfFile(@"
Map; 4900553433511235353734; tm01
TypeJ; temperature_tm01_01; tm01; 1
Math; temperature_tm01_01_math; temperature_tm01_01 + 1
".SplitNewLine(StringSplitOptions.None));

            // Act
            var boardStateNames = ioconf.GetBoardStateNames("temperature_tm01_01_math").ToList();

            // Assert
            Assert.AreEqual(1, boardStateNames.Count());
            Assert.AreEqual("tm01_state", boardStateNames.First());
        }

        [TestMethod]
        public void SupportsTags()
        {
            var loader = new IOconfLoader();
            loader.AddLoader(IOconfRowWithListArgument.ConfigName, (r, l) => new IOconfRowWithListArgument(r, l));
            var ioconf = new IOconfFile(loader, @"
Map;fakeserial;ac01
Map;fakeserial;ac02
Map;fakeserial;ac03
Heater; LeftHeater16; ac01;01;850; tags:phase1 ovenarea4 ovenheaters // Freely choose tags to assign config entries to groups
Heater; LeftHeater17; ac02;01;850; tags:phase2 ovenarea4 ovenheaters
Heater; LeftHeater18; ac02;02;850; tags:phase2 ovenarea4 ovenheaters
Heater; ExternalHeater01; ac03;01;850; tags:phase3 bsarea

Code;power_control;0.1190.0;pc2
pc2; Heaters_onoff; tagfields:phase2;suffix:onoff // Expands entries for tag `phase2`, generating entries like `LeftHeater17_onoff, LeftHeater18_onoff, ...`
pc2; Heaters_currentsampled; tagfields:phase2;suffix:sampledcurrent // Expands entries with `sampledcurrent` suffix

Code; power_control_sampling; 0.1190.0; pcs2
pcs2; Heaters_onoff; tagfields:phase2;suffix:onoff
pcs2; Heaters_currentsampled; tagfields:phase2;suffix:sampledcurrent

RowWithList; mylist; LeftHeater16;LeftHeater17;LeftHeater18
RowWithList; mylist2; tagfields:ovenheaters //should be equivalent to the previous one

Expand;math1;math2;math3;-;Math;$name;$name // Creates a new line for all labels (the empty - separates the list from the command)
Expand;tagfields:phase2;suffix:sampledcurrent;Math;$name;$name // Creates a new line for each tag, generating lines like: 
".SplitNewLine(StringSplitOptions.None));
            AssertRowValuesByNameAndType("LeftHeater17_onoff,LeftHeater18_onoff", ioconf, "pc2", "Heaters_onoff");
            AssertRowValuesByNameAndType("LeftHeater17_onoff,LeftHeater18_onoff", ioconf, "pcs2", "Heaters_onoff");
            AssertRowValuesByNameAndType("LeftHeater17_sampledcurrent,LeftHeater18_sampledcurrent", ioconf, "pc2", "Heaters_currentsampled");
            AssertRowValuesByNameAndType("LeftHeater17_sampledcurrent,LeftHeater18_sampledcurrent", ioconf, "pcs2", "Heaters_currentsampled");
            AssertRowValuesByName("LeftHeater16,LeftHeater17,LeftHeater18", ioconf, "mylist");
            AssertRowValuesByName("LeftHeater16,LeftHeater17,LeftHeater18", ioconf, "mylist2");
            AssertRowValuesByNameAndType("math1", ioconf, "Math", "math1");
            AssertRowValuesByNameAndType("math2", ioconf, "Math", "math2");
            AssertRowValuesByNameAndType("math3", ioconf, "Math", "math3");
            AssertRowValuesByNameAndType("LeftHeater17_sampledcurrent", ioconf, "Math", "LeftHeater17_sampledcurrent");
            AssertRowValuesByNameAndType("LeftHeater18_sampledcurrent", ioconf, "Math", "LeftHeater18_sampledcurrent");

            static void AssertRowValuesByNameAndType(string values, IOconfFile conf, string type, string name) =>
                Assert.AreEqual(values,GetRowValues(conf, r => r.Type == type && r.Name == name, $"{type}-{name}"));
            static void AssertRowValuesByName(string values, IOconfFile conf, string name) =>
                Assert.AreEqual(values, GetRowValues(conf, r => r.Name == name, name));
            static string GetRowValues(IOconfFile conf, Expression<Func<IOconfRow, bool>> predicate, string msg) => 
                string.Join(',', 
                    (conf.GetEntries<IOconfRow>().SingleOrDefault(predicate.Compile()) 
                        ?? throw new ArgumentException($"Row not found: {msg}"))
                    .ToList()[2..]);
        }

        public sealed class IOconfRowWithListArgument : IOconfRow
        {
            public const string ConfigName = "RowWithList";
            public IOconfRowWithListArgument(string row, int lineNum) : base(row, lineNum, ConfigName)
            {
                Format = $"{ConfigName};Name;List";
                ExpandsTags = true;
                var list = ToList();
                if (list.Count < 3 || string.IsNullOrEmpty(list[2]))
                    throw new FormatException($"Wrong format: {Row}. {Format}");
            }
        }
    }
}
