using System;
using System.Collections.Generic;
using System.Linq;

namespace CA_DataUploaderLib.IOconf
{
    public class IOConfPressure : IOconfInput
    { 
        public IOConfPressure(string row, int lineNum) : base(row, lineNum, "Pressure")
        {
            var list = ToList();
            Name = list[1];
            BoxName = list[2];
            SetMap(BoxName);
            if (list[3].ToLower() == "skip")
            {
                Skip = true;
            }
            else
            {
                if (!int.TryParse(list[3], out PortNumber)) throw new Exception("IOConfPressure: wrong port number: " + row);
            }
        }
    }
}
