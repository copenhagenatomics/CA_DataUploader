using System;
using System.Collections.Generic;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfHeater : IOconfOut230Vac
    {
        public IOconfHeater(string row, int lineNum) : base(row, lineNum, "Heater")
        {
            var list = ToList();
            if (!int.TryParse(list[4], out MaxTemperature)) throw new Exception("IOconfHeater: missing max temperature: " + row);
            if (list.Count > 5 && !int.TryParse(list[5], out MaxOnInterval)) throw new Exception("IOconfHeater: bad max on interfal: " + row);
        }

        public int MaxTemperature;
        public int MaxOnInterval = 30;

        public IOconfInput AsConfInput()
        {
            return new IOconfInput(Row, LineNumber, "Current");
        }
    }
}
