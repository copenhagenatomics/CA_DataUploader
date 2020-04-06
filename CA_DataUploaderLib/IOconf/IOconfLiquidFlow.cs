using System;
using System.Collections.Generic;
using System.Linq;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfLiquidFlow : IOconfInput
    {
        public IOconfLiquidFlow(string row, int lineNum) : base(row, lineNum, "LiquidFlow")
        {
            var list = ToList();
            Name = list[1];
            BoxName = list[2];
            SetMap(BoxName);
            if (!int.TryParse(list[3], out PortNumber)) throw new Exception("IOconfLiquidFlow: wrong port number: " + row);

        }
        
    }
}
