using System.Collections.Generic;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfSwitchboardSensor : IOconfInput
    {
        private readonly string RmsName;
        private readonly string MaxName;
        private readonly string Subsystem;

        public IOconfSwitchboardSensor(string row, int lineNum) : base(row, lineNum, "SwitchboardSensor", false, true, IOconfOut230Vac.GetNewSwitchboardBoardSettings(row))
        {
            format = "SwitchboardSensor;Name;BoxName;[SubsystemName]";
            var list = ToList();
            Subsystem = list.Count > 3 ? list[3].ToLower() : "vibration";
        }

        /// <summary>get expanded conf entries that include both the rms and max within the 100 milliseconds read cycle</summary>
        /// <remarks>the returned entries have port numbers that correspond to the ones returned by the dc switchboard as parsed by the IOconfOut230Vac.SwitchBoardResponseParser</remarks>
        public IEnumerable<IOconfInput> GetExpandedConf() => new [] {
            NewPortInput($"{Name}_rms", 6),
            NewPortInput($"{Name}_maxIn100ms", 7),
            };

        private IOconfInput NewPortInput(string name, int portNumber) => new IOconfInput(Row, LineNumber, Type, false, false, null)
            { Name = name, BoxName = BoxName, Map = Map, PortNumber = portNumber, SubsystemOverride = Subsystem };
    }
}