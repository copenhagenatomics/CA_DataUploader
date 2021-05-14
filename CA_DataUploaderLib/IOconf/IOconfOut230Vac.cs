using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using CA_DataUploaderLib.Extensions;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfOut230Vac : IOconfOutput
    {
        public IOconfOut230Vac(string row, int lineNum, string type) : base(row, lineNum, type, true, 
            new BoardSettings() { Parser = SwitchBoardResponseParser.Default }) 
        {
            CurrentSensorName = Name + "_current";
            SwitchboardOnOffSensorName = Name + "_SwitchboardOn/Off";
            BoardStateSensorName = BoxName + "_state"; // this must match the state sensor names returned by BaseSensorBox
        }

        public string CurrentSensorName { get; }
        public string SwitchboardOnOffSensorName { get; }
        public string BoardStateSensorName { get; } 
        public IEnumerable<IOconfInput> GetExpandedInputConf()
        { // note "_On/Off" is not included as its not an input but the current expected on/off state as seen by the control loop.
            yield return NewPortInput(CurrentSensorName, 0 + PortNumber);
            yield return NewPortInput(SwitchboardOnOffSensorName, 4 + PortNumber);
        }

        /// <remarks>This config is general for the board, so caller must make sure to use a single instance x board</remarks>
        public IOconfInput GetBoardTemperatureInputConf() => NewPortInput(Map.BoxName + "_temperature", 8);
        private IOconfInput NewPortInput(string name, int portNumber) => new IOconfInput(Row, LineNumber, Type, false, false, null) 
            { Name = name, BoxName = BoxName, Map = Map, PortNumber = portNumber };

        public class SwitchBoardResponseParser : BoardSettings.LineParser
        {
            // "P1=0.06A P2=0.05A P3=0.05A P4=0.06A 0, 1, 0, 1, 25.87"
            // first 4 are currents, then comes 4 switchboard on/off values and then temperature. 
            // the last 5 values are not there in older versions of the switchboard software.
            private const string _SwitchBoxPattern = "P1=(-?\\d\\.\\d\\d)A P2=(-?\\d\\.\\d\\d)A P3=(-?\\d\\.\\d\\d)A P4=(-?\\d\\.\\d\\d)A(?: ([01]), ([01]), ([01]), ([01])(?:, (-?\\d+.\\d\\d))?)?";
            private static readonly Regex _switchBoxCurrentsRegex = new Regex(_SwitchBoxPattern);
            public new static SwitchBoardResponseParser Default { get; } = new SwitchBoardResponseParser();

            // returns currents 0-3, states 0-3, board temperature
            public override List<double> TryParseAsDoubleList(string line)
            {
                var match = _switchBoxCurrentsRegex.Match(line);
                if (!match.Success) 
                    return null; 
                return GetValuesFromGroups(match.Groups);
            }

            public override bool MatchesValuesFormat(string line) => _switchBoxCurrentsRegex.IsMatch(line);

            private static List<double> GetValuesFromGroups(GroupCollection groups)
            {
                var data = new List<double>(9);
                var valueGroups = groups.Cast<Group>().Skip(1).ToList(); 
                data.AddRange(valueGroups.Take(4).Select(x => x.Value.ToDouble())); 
                data.AddRange(valueGroups.Skip(4).Take(4).Select(x => x.Success ? (double)x.Value.ToInt() : 10000d)); 
                data.Add(valueGroups[8].Success ? valueGroups[8].Value.ToDouble() : 10000d);
                return data;
            }
        }
    }
}
