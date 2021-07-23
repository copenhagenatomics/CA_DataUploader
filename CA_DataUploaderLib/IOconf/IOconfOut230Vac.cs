using System.Collections.Generic;
using System.Text.RegularExpressions;
using CA_DataUploaderLib.Extensions;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfOut230Vac : IOconfOutput
    {
        public IOconfOut230Vac(string row, int lineNum, string type) : base(row, lineNum, type, true, 
            new BoardSettings() { Parser = new SwitchBoardResponseParser(!row.Contains("showConfirmations")), ValuesEndOfLineChar = "\r" }) 
        {
            CurrentSensorName = Name + "_current";
            SwitchboardOnOffSensorName = Name + "_SwitchboardOn/Off";
            BoardStateSensorName = BoxName + "_state"; // this must match the state sensor names returned by BaseSensorBox
        }

        public string CurrentSensorName { get; }
        public string SwitchboardOnOffSensorName { get; }
        public string BoardStateSensorName { get; } 
        public bool HasOnSafeState { get; protected set; } = false;
        public IEnumerable<IOconfInput> GetExpandedInputConf()
        { // note "_On/Off" is not included as its not an input but the current expected on/off state as seen by the control loop.
            yield return NewPortInput(CurrentSensorName, 0 + PortNumber);
            yield return NewPortInput(SwitchboardOnOffSensorName, 4 + PortNumber);
        }

        /// <remarks>This config is general for the board, so caller must make sure to use a single instance x board</remarks>
        public IOconfInput GetBoardTemperatureInputConf() => NewPortInput(Map.BoxName + "_temperature", 9);
        private IOconfInput NewPortInput(string name, int portNumber) => new IOconfInput(Row, LineNumber, Type, false, false, null) 
            { Name = name, BoxName = BoxName, Map = Map, PortNumber = portNumber };

        public class SwitchBoardResponseParser : BoardSettings.LineParser
        {
            // "P1=0.06A P2=0.05A P3=0.05A P4=0.06A 0, 1, 0, 1, 25.87"
            // first 4 are currents, then comes 4 switchboard on/off values and then temperature. 
            // the last 5 values are not there in older versions of the switchboard software.
            private const string _SwitchBoxPattern = "P1=(-?\\d\\.\\d\\d)A P2=(-?\\d\\.\\d\\d)A P3=(-?\\d\\.\\d\\d)A P4=(-?\\d\\.\\d\\d)A(?: ([01]), ([01]), ([01]), ([01])(?:, (-?\\d+.\\d\\d))?)?";
            private static readonly Regex _switchBoxCurrentsRegex = new Regex(_SwitchBoxPattern);
            private const string _commandConfirmationPattern = "p[1-4] (?:auto off)|(?:off)|(?:on(?: \\d+)?)";
            private static readonly Regex _commandConfirmationRegex = new Regex(_commandConfirmationPattern);
            private readonly bool _expectCommandConfirmations;

            public new static SwitchBoardResponseParser Default { get; } = new SwitchBoardResponseParser(true);
            // setting it to *not* expect command confirmations will normally cause them to be shown in the console + logs,
            // which is mostly useful for debugging purposes.
            public SwitchBoardResponseParser (bool expectCommandConfirmations)
            {
                _expectCommandConfirmations = expectCommandConfirmations;
            }

            // returns currents 0-3, states 0-3, board temperature
            public override List<double> TryParseAsDoubleList(string line)
            {
                var match = _switchBoxCurrentsRegex.Match(line);
                if (!match.Success) 
                    return TryParseOnlyNumbersAsDoubleList(line);
                return GetValuesFromGroups(match.Groups);
            }

            // returns currents 0-3, states 0-3, board temperature
            public List<double> TryParseOnlyNumbersAsDoubleList(string line)
            {
                var numbers = base.TryParseAsDoubleList(line); //the base implementation deals with any amount of simple numbers separated by commas.
                var missingValues = 0;
                if (numbers == null)
                    return null;
                if (numbers.Count == 4)
                    missingValues = 5; // missing states and board temperature
                else if (numbers.Count == 5)
                    missingValues = 4; // missing states
                else if (numbers.Count == 8)
                    missingValues = 1; // missing board temperature
                else if (numbers.Count == 9)
                    return numbers;
                else
                    return null; // invalid line 
                
                for (int i = 0; i < missingValues; i++)
                    numbers.Add(10000);
                return numbers;
            }

            public override bool MatchesValuesFormat(string line) => _switchBoxCurrentsRegex.IsMatch(line);
            private static List<double> GetValuesFromGroups(GroupCollection groups)
            {
                var data = new List<double>(9);
                for (int i = 1; i < 10; i++)
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
