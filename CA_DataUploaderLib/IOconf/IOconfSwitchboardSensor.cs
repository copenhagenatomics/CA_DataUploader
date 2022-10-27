#nullable enable
using System.Collections.Generic;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfSwitchboardSensor : IOconfInput.Expandable
    {
        private readonly string Subsystem;

        public IOconfSwitchboardSensor(string row, int lineNum) : base(row, lineNum, "SwitchboardSensor", false, IOconfOut230Vac.GetNewSwitchboardBoardSettings(row))
        {
            Format = "SwitchboardSensor;Name;BoxName;[SubsystemName]";
            var list = ToList();
            Subsystem = list.Count > 3 ? list[3].ToLower() : "vibration";
        }

        /// <summary>get expanded conf entries that include both the rms and max within the 100 milliseconds read cycle</summary>
        /// <remarks>the returned entries have port numbers that correspond to the ones returned by the dc switchboard as parsed by the IOconfOut230Vac.SwitchBoardResponseParser</remarks>
        public IEnumerable<IOconfInput> GetExpandedConf() => new [] {
            NewInput($"{Name}_rms", 6, Subsystem),
            NewInput($"{Name}_maxIn100ms", 7, Subsystem),
            };
    }
}