#nullable enable
using System.Collections.Generic;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfOut230Vac : IOconfOutput
    {
        public IOconfOut230Vac(string row, int lineNum, string type) : base(row, lineNum, type, true, GetSwitchboardBoardSettings()) 
        {
            CurrentSensorName = Name + "_current";
        }

        public string CurrentSensorName { get; }
        public bool IsSwitchboardControllerOutput { get; }
        public override IEnumerable<IOconfInput> GetExpandedInputConf()
        { // note "_onoff" is not included as its not an input but the current expected on/off state as seen by the control loop.
            yield return NewPortInput(CurrentSensorName, 0 + PortNumber);
        }

        public override IEnumerable<string> GetExpandedSensorNames(IIOconf ioconf)
        {
            yield return Name;
            foreach (var name in base.GetExpandedSensorNames(ioconf))
                yield return name;
        }

        /// <remarks>This config is general for the board, so caller must make sure to use a single instance x board</remarks>
        public IOconfInput GetBoardTemperatureInputConf() => NewPortInput(Map.BoxName + "_temperature", 5);
        public static BoardSettings GetSwitchboardBoardSettings() => 
            new() { Parser = SwitchBoardResponseParser.Default, ValuesEndOfLineChar = '\r' };
        public class SwitchBoardResponseParser : BoardSettings.LineParser
        {
            public new static SwitchBoardResponseParser Default { get; } = new SwitchBoardResponseParser();

            // returns currents 0-3, board temperature (, sensorport_rms, sensorport_max)
            public override (List<double>?, uint) TryParseAsDoubleList(string line)
            {
                return TryParseOnlyNumbersAsDoubleList(line);
            }

            // returns currents 0-3, board temperature (, sensorport_rms, sensorport_max)
            //      or currents 0-5(/9)
            public (List<double>?, uint) TryParseOnlyNumbersAsDoubleList(string line)
            {
                var (numbers, status) = base.TryParseAsDoubleList(line); //the base implementation deals with any amount of simple numbers separated by commas.
                if (numbers == null)
                    return (null, 0);
                if (numbers.Count < 4)
                    return (null, 0); //invalid line, must have at least 4 currents
                if (numbers.Count == 4)
                    numbers.Add(10000);//some versions did not report temperature
                return (numbers, status);
            }
        }
    }
}
