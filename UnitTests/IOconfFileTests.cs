using Microsoft.VisualStudio.TestTools.UnitTesting;
using CA_DataUploaderLib.Extensions;
using CA_DataUploaderLib.IOconf;
using System;
using System.Linq;
using System.Linq.Expressions;
using System.Collections.Generic;

namespace UnitTests
{
    [TestClass]
    public class IOconfFileTests
    {
        private static IEnumerable<IOconfRow> ParseLines(List<string> lines) 
            => new IOconfFile(lines).GetEntries<IOconfRow>().Where(r => r is not IOconfRPiTemp); //we exclude rpi temp, as tests using this dont expect that to be added automatically
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
pc2; Heaters_onoff; expandtag{phase2,$name_onoff} // Expands entries for tag `phase2`, generating entries like `LeftHeater17_onoff, LeftHeater18_onoff, ...`
pc2; Heaters_currentsampled; expandtag{phase2,$name_sampledcurrent} // Expands entries with `sampledcurrent` suffix

Code; power_control_sampling; 0.1190.0; pcs2
pcs2; Heaters_onoff; expandtag{phase2,$name_onoff}
pcs2; Heaters_currentsampled; expandtag{phase2,$name_sampledcurrent}

RowWithList; mylist; LeftHeater16;LeftHeater17;LeftHeater18
RowWithList; mylist2; expandtag{ovenheaters} //should be equivalent to the previous one (note it uses the default expression $name)
RowWithList; mylist3; expandtag{ovenheaters,$name_$matchingtag(phase1 phase2 phase3)}//each matchingtag outputs whichever of those tags the item has e.g. LeftHeater16_phase1, LeftHeater17_phase2, LeftHeater18_phase2
RowWithList; mylist4; expandtag{ovenheaters,$name_current;$name_onoff} //should be equivalent to the previous one (note it uses the default expression $name)

ExpandLines;math1,math2,math3;Math;$name;$name // Creates a new line for all labels (the empty - separates the list from the command)
ExpandTagLines;phase2;Math;$name_sampledcurrent;$name_sampledcurrent //also creates multiple math lines
ExpandTagLines;phase2;Math;$name_math;$name_ing //same, but diff suffixes
Math;bigsum;12 + expandtag{ovenheaters,separator:+,$name_current} // 12 + sum of currents
Math;bigsum2;12 + expandtag{ovenheaters,separator:+,($name_current + 2*$name_onoff)} // 12 + sum of (current + 2*onoff)
Math;bigsum3;12 + expandtag{ovenheaters,separator:+,(2*$name_current*$name_onoff)} // 12 + sum of (2*current*onoff)
Math;bigsum4;12 + 2*(expandtag{ovenheaters,separator:+,$name_current})*(expandtag{ovenheaters,separator:+,$name_onoff}) // 12 + 2*sum of (current)*sum of(onoff)
Math;bigsum5;12 + expandtag{ovenheaters,separator:+,(2*$name_current*$name_onoff)} * 1282 // 12 + sum of (2*current*onoff) * 1282
Math;bigsum6;if(expandtag{ovenheaters,separator: && ,(2*$name_current*$name_onoff > 2)},ac03_state,ac02_state) // equivalent to if (all(2*current*onoff > 2),a,b)
RedundantSensors;redundant;expandtag{ovenheaters}

//later one could add syntatic suggar for some, like expandtagsum (similar to how expandtaglines skips the need of the separator)
".SplitNewLine(StringSplitOptions.None));
            AssertRowValuesByNameAndType(ioconf, "pc2", "Heaters_onoff", "LeftHeater17_onoff,LeftHeater18_onoff");
            AssertRowValuesByNameAndType(ioconf, "pcs2", "Heaters_onoff", "LeftHeater17_onoff,LeftHeater18_onoff");
            AssertRowValuesByNameAndType(ioconf, "pc2", "Heaters_currentsampled", "LeftHeater17_sampledcurrent,LeftHeater18_sampledcurrent");
            AssertRowValuesByNameAndType(ioconf, "pcs2", "Heaters_currentsampled", "LeftHeater17_sampledcurrent,LeftHeater18_sampledcurrent");
            AssertRowValuesByName(ioconf, "mylist", "LeftHeater16,LeftHeater17,LeftHeater18");
            AssertRowValuesByName(ioconf, "mylist2", "LeftHeater16,LeftHeater17,LeftHeater18");
            AssertRowValuesByName(ioconf, "mylist3", "LeftHeater16_phase1,LeftHeater17_phase2,LeftHeater18_phase2");
            AssertRowValuesByName(ioconf, "mylist4", "LeftHeater16_current,LeftHeater16_onoff,LeftHeater17_current,LeftHeater17_onoff,LeftHeater18_current,LeftHeater18_onoff");
            AssertRowValuesByNameAndType(ioconf, "Math", "math1", "math1");
            AssertRowValuesByNameAndType(ioconf, "Math", "math2", "math2");
            AssertRowValuesByNameAndType(ioconf, "Math", "math3", "math3");
            AssertRowValuesByNameAndType(ioconf, "Math", "LeftHeater17_sampledcurrent", "LeftHeater17_sampledcurrent");
            AssertRowValuesByNameAndType(ioconf, "Math", "LeftHeater18_sampledcurrent", "LeftHeater18_sampledcurrent");
            AssertRowValuesByNameAndType(ioconf, "Math", "LeftHeater17_math", "LeftHeater17_ing");
            AssertRowValuesByNameAndType(ioconf, "Math", "LeftHeater18_math", "LeftHeater18_ing");
            AssertRowValuesByNameAndType(ioconf, "Math", "bigsum2", "12 + (LeftHeater16_current + 2*LeftHeater16_onoff)+(LeftHeater17_current + 2*LeftHeater17_onoff)+(LeftHeater18_current + 2*LeftHeater18_onoff)");
            AssertRowValuesByNameAndType(ioconf, "Math", "bigsum3", "12 + (2*LeftHeater16_current*LeftHeater16_onoff)+(2*LeftHeater17_current*LeftHeater17_onoff)+(2*LeftHeater18_current*LeftHeater18_onoff)");
            AssertRowValuesByNameAndType(ioconf, "Math", "bigsum4", "12 + 2*(LeftHeater16_current+LeftHeater17_current+LeftHeater18_current)*(LeftHeater16_onoff+LeftHeater17_onoff+LeftHeater18_onoff)");
            AssertRowValuesByNameAndType(ioconf, "Math", "bigsum5", "12 + (2*LeftHeater16_current*LeftHeater16_onoff)+(2*LeftHeater17_current*LeftHeater17_onoff)+(2*LeftHeater18_current*LeftHeater18_onoff) * 1282");
            AssertRowValuesByNameAndType(ioconf, "Math", "bigsum6", "if((2*LeftHeater16_current*LeftHeater16_onoff > 2) && (2*LeftHeater17_current*LeftHeater17_onoff > 2) && (2*LeftHeater18_current*LeftHeater18_onoff > 2),ac03_state,ac02_state)");
            AssertRowValuesByNameAndType(ioconf, "RedundantSensors", "redundant", "LeftHeater16,LeftHeater17,LeftHeater18");

            static void AssertRowValuesByNameAndType(IOconfFile conf, string type, string name, string values) =>
                Assert.AreEqual(values,GetRowValues(conf, r => r.Type == type && r.Name == name, $"{type}-{name}"));
            static void AssertRowValuesByName(IOconfFile conf, string name, string values) =>
                Assert.AreEqual(values, GetRowValues(conf, r => r.Name == name, name));
            static string GetRowValues(IOconfFile conf, Expression<Func<IOconfRow, bool>> predicate, string msg) => 
                string.Join(',', GetRequiredRow(conf, predicate, msg).ToList()[2..]);
            static IOconfRow GetRequiredRow(IOconfFile conf, Expression<Func<IOconfRow, bool>> predicate, string msg) =>
                conf.GetEntries<IOconfRow>().SingleOrDefault(predicate.Compile()) ?? throw new ArgumentException($"Row not found: {msg}");
        }

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
            Assert.AreEqual(405, math.Calculate(new() { { "heater1", 400 } }));
        }

        [TestMethod]
        public void CanLoadGenericOutputLine()
        {
            var rowsEnum = ParseLines(["Map;fakeserial;realacbox2","GenericOutput;generic_ac_on;realacbox2;0;p1 $heater1_onoff 3"]);
            var rows = rowsEnum.ToArray();
            Assert.AreEqual(2, rows.Length);
            Assert.IsInstanceOfType(rows[1], typeof(IOconfGenericOutput));
            var output = (IOconfGenericOutput)rows[1];
            Assert.AreEqual("generic_ac_on", output.Name);
            Assert.AreEqual(0, output.DefaultValue);
            Assert.AreEqual("heater1_onoff", output.TargetField);
            Assert.AreEqual("p1 5 3", output.GetCommand(5));
        }

        [TestMethod]
        public void CanLoadGenericOutputLineWithBraces()
        {
            var rowsEnum = ParseLines(["GenericOutput;generic_ac_on;realacbox2;0;p1 on 3 ${heater1_onoff}00%","Map;fake1;realacbox2"]);
            var rows = rowsEnum.ToArray();
            Assert.AreEqual(2, rows.Length);
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
            var loader = new IOconfLoader();
            loader.AddLoader("Mathing", (row, lineIndex) => new IOConfMathing(row, lineIndex));
            var ioconf = new IOconfFile(loader, ["Mathing;mymath;heater1 + 5"]);
            var rows = ioconf.GetEntries<IOconfRow>().ToArray();
            Assert.AreEqual(2, rows.Length);
            Assert.IsInstanceOfType(rows[0], typeof(IOConfMathing));
        }

        [TestMethod]
        public void CanLoadCurrentLine()
        {
            var rowsEnum = ParseLines(["Current;current_ct01;ct01;2;300","Map;fakeserial;ct01"]);
            var rows = rowsEnum.ToArray();
            Assert.AreEqual(2, rows.Length);
            Assert.IsInstanceOfType(rows[0], typeof(IOconfCurrent));
            var current = (IOconfCurrent)rows[0];
            Assert.AreEqual("current_ct01", current.Name);
            Assert.AreEqual(2, current.PortNumber);
        }

        private class IOConfMathing(string row, int lineIndex) : IOconfRow(row, lineIndex, "Mathing")
        {
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
