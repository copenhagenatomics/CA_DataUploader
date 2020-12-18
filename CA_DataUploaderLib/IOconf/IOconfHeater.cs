using CA_DataUploaderLib.Extensions;
using System;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfHeater : IOconfOut230Vac
    {
        public IOconfHeater(string row, int lineNum) : base(row, lineNum, "Heater")
        {
            format = "Heater;Name;BoxName;port number;MaxTemperature;CurrentSensingNoiseTreshold";

            var list = ToList();
            if (list.Count < 5 || !int.TryParse(list[4], out MaxTemperature)) throw new Exception("IOconfHeater: missing max temperature: " + row);
            if (list.Count == 6 && !list[5].TryToDouble(out CurrentSensingNoiseTreshold)) throw new Exception("IOconfHeater: failed to parse CurrentSensingNoiseTreshold: " + row);
        }

        public int MaxTemperature;
        /// <summary>the heater is considered off below this current level (in amps)</summary>
        /// <remarks>at the time of writting, this can not be lowered for the current version of the AC switchboard</remarks>
        public readonly double CurrentSensingNoiseTreshold = 0.4;

        public IOconfInput AsConfInput()
        {
            return new IOconfInput(Row, LineNumber, "Heater")
            {
                Name = Name + "_Power"
            };
        }
    }
}
