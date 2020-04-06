using System;
using System.Collections.Generic;
using System.Linq;

namespace CA_DataUploaderLib.IOconf
{
   public class IOconfScale : IOconfInput
    {
        public IOconfScale(string row, int lineNum) : base(row, lineNum, "Scale")
        {
            var list = ToList();
            Name = list[1];
            BoxName = list[2];
            SetMap(BoxName);
            if (!int.TryParse(list[3], out PortNumber)) throw new Exception("IOconfScale: wrong port number: " + row);

        }
        
    }
}
