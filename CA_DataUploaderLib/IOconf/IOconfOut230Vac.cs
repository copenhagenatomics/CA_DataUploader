#nullable enable
using System.Collections.Generic;
using System.Text.RegularExpressions;
using CA_DataUploaderLib.Extensions;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfOut230Vac : IOconfOutput
    {
        public IOconfOut230Vac(string row, int lineNum, string type) : base(row, lineNum, type, true, GetNewSwitchboardBoardSettings(row)) 
        {
            CurrentSensorName = Name + "_current";
            BoardStateSensorName = BoxName + "_state"; // this must match the state sensor names returned by BaseSensorBox
        }

        public string CurrentSensorName { get; }
        public string BoardStateSensorName { get; } 
        public bool IsSwitchboardControllerOutput { get; }
        public IEnumerable<IOconfInput> GetExpandedInputConf()
        { // note "_onoff" is not included as its not an input but the current expected on/off state as seen by the control loop.
            yield return NewPortInput(CurrentSensorName, 0 + PortNumber);
        }

        /// <remarks>This config is general for the board, so caller must make sure to use a single instance x board</remarks>
        public IOconfInput GetBoardTemperatureInputConf() => NewPortInput(Map.BoxName + "_temperature", 5);
        public static BoardSettings GetNewSwitchboardBoardSettings(string row) => 
            new() { Parser = new SwitchBoardResponseParser(!row.Contains("showConfirmations")), ValuesEndOfLineChar = '\r' };
        public class SwitchBoardResponseParser : BoardSettings.LineParser
        {
            // old response format "P1=0.06A P2=0.05A P3=0.05A P4=0.06A 0, 1, 0, 1, 25.87"
            // first 4 are currents, then comes 4 switchboard on/off values and then temperature. 
            // some versions of the old switchboards did not return the last 5 values
            // the 4 switchboard on/off values will be ignored (non capturing groups) as we don't plan to use those moving forward
            private const string _oldSwitchBoxPattern = @"P1=(-?\d\.\d\d)A P2=(-?\d\.\d\d)A P3=(-?\d\.\d\d)A P4=(-?\d\.\d\d)A(?: [01], [01], [01], [01](?:, (-?\d+.\d\d))?)?";
            private static readonly Regex _oldswitchBoxCurrentsRegex = new(_oldSwitchBoxPattern);
            private const string _commandConfirmationPattern = @"^\s*p[1-4] (?:(?:auto off)|(?:off)|(?:on(?: \d+)?))\s*$";
            private static readonly Regex _commandConfirmationRegex = new(_commandConfirmationPattern);
            private readonly bool _expectCommandConfirmations;

            public new static SwitchBoardResponseParser Default { get; } = new SwitchBoardResponseParser(true);
            // setting it to *not* expect command confirmations will normally cause them to be shown in the console + logs,
            // which is mostly useful for debugging purposes.
            public SwitchBoardResponseParser (bool expectCommandConfirmations)
            {
                _expectCommandConfirmations = expectCommandConfirmations;
            }

            // returns currents 0-3, board temperature (, sensorport_rms, sensorport_max)
            public override List<double>? TryParseAsDoubleList(string line)
            {
                var match = _oldswitchBoxCurrentsRegex.Match(line);
                if (!match.Success) 
                    return TryParseOnlyNumbersAsDoubleList(line);
                return GetValuesFromGroups(match.Groups);
            }

            // returns currents 0-3, board temperature (, sensorport_rms, sensorport_max)
            public List<double>? TryParseOnlyNumbersAsDoubleList(string line)
            {
                var numbers = base.TryParseAsDoubleList(line); //the base implementation deals with any amount of simple numbers separated by commas.
                if (numbers == null)
                    return null;
                if (numbers.Count < 4)
                    return null; //invalid line, must have at least 4 currents
                if (numbers.Count == 4)
                    numbers.Add(10000);//some versions did not report temperature
                return numbers;
            }

            public override bool MatchesValuesFormat(string line) => _oldswitchBoxCurrentsRegex.IsMatch(line);
            // returns currents 0-3, board temperature
            private static List<double> GetValuesFromGroups(GroupCollection groups)
            {
                var data = new List<double>(5);
                for (int i = 1; i < 6; i++)
                    data.Add(groups[i].Success ? groups[i].Value.ToDouble() : 10000d);
                return data;
            }
            /// <summary>for now consider all command confirmations expected</summary>
            public override bool IsExpectedNonValuesLine(string line)
            {
                if (line == "\n")
                    return true; // the switchboard is currently sending \r\n\r at the end of a command confirmation (the last \r is being printed before the next current line)
                return _expectCommandConfirmations && _commandConfirmationRegex.IsMatch(line); // note unlike for value lines where we only get \r, in these lines we get \n\r and this also ignores those
            }
        }
    }
}
