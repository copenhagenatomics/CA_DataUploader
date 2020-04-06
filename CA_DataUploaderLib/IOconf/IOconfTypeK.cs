using System;
using System.Collections.Generic;
using System.Linq;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfTypeK : IOconfInput
    {
        public IOconfTypeK(string row) : base(row, "TypeK")
        {
            var list = ToList();
            Name = list[1];
            BoxName = list[2];
            SetMap(BoxName);
            if (list[3].ToLower() == "skip")
            {
                Skip = true;   // BaseSensorBox will skip reading from this box. Data is comming from other sensors through ProcessLine() instead. You must skip all lines from this box in IO.conf, else it will not work.
            }
            else
            {
                if (!int.TryParse(list[3], out PortNumber)) throw new Exception("IOconfTypeK: wrong port number: " + row);
                if (PortNumber < 0 || PortNumber > 16) throw new Exception("IOconfTypeK: invalid port number: " + row);
            }
        }

    }
}
