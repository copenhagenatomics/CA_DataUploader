using System;
using System.Collections.Generic;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfHeater : IOconfOut230Vac
    {
        public IOconfHeater(string row) : base(row, "Heater")
        {
            var list = ToList();
            if (!int.TryParse(list[4], out MaxTemperature)) throw new Exception("IOconfHeater: missing max temperature: " + row);
        }

        public int MaxTemperature;
    }
}
