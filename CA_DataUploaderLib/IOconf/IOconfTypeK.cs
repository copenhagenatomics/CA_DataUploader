using System;
using System.Collections.Generic;
using System.Linq;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfTypeK : IOconfInput
    {
        public IOconfTypeK(string row, IEnumerable<IOconfMap> map) : base(row, "TypeK")
        {
            var list = ToList();
            Name = list[1];
            BoxName = list[2];
            SetMap(BoxName, map);
            if (!int.TryParse(list[3], out PortNumber)) throw new Exception("IOconfTypeK: wrong port number: " + row);

            if (list.Count > 4)
                HeaterName = list[4];
        }

        public string HeaterName;  // bad code, remove later. 
    }
}
