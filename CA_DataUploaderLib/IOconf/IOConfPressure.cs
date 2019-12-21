using System;
using System.Collections.Generic;
using System.Linq;

namespace CA_DataUploaderLib.IOconf
{
    public class IOConfPressure : IOconfInput
    { 
        public IOConfPressure(string row, IEnumerable<IOconfMap> map) : base(row, "Pressure")
        {
            var list = ToList();
            Name = list[1];
            BoxName = list[2];
            Map = map.Single(x => x.BoxName == BoxName);
            if (!int.TryParse(list[3], out PortNumber)) throw new Exception("IOConfPressure: wrong port number: " + row);
        }
        
    }
}
