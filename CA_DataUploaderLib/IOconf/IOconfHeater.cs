using System;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfHeater : IOconfOut230Vac
    {
        public IOconfHeater(string row, int lineNum) : base(row, lineNum, "Heater")
        {
            format = "Heater;Name;BoxName;port number;MaxTemperature";

            var list = ToList();
            if (list.Count < 5 || !int.TryParse(list[4], out MaxTemperature)) throw new Exception("IOconfHeater: missing max temperature: " + row);
        }

        public int MaxTemperature;

        public IOconfInput AsConfInput()
        {
            return new IOconfInput(Row, LineNumber, "Heater")
            {
                Name = Name + "_Power"
            };
        }
    }
}
