using CA_DataUploaderLib.Extensions;
using System;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfHeater : IOconfOut230Vac
    {
        public IOconfHeater(string row, int lineNum) : base(row, lineNum, "Heater")
        {
            //note: the format used to be "Heater;Name;BoxName;port number;MaxTemperature;CurrentSensingNoiseTreshold",
            //but CurrentSensingNoiseTreshold got removed as its no longer relevant to the current decision logic
            //(used to influence repeat of off commands, but now they run unconditionally by the SwitchboardController.
            Format = "Heater;Name;BoxName;port number;MaxTemperature";

            var list = ToList();
            if (list.Count < 5 || !int.TryParse(list[4], out MaxTemperature)) throw new Exception("IOconfHeater: missing max temperature: " + row);
        }

        public readonly int MaxTemperature;
    }
}
