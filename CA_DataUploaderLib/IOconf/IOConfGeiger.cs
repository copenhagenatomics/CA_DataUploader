using System;

namespace CA_DataUploaderLib.IOconf
{
    public class IOConfGeiger : IOconfInput
    { 
        public IOConfGeiger(string row, int lineNum) : base(row, lineNum, "Geiger")
        {
            format = "Geiger;Name;BoxName;[port number]";

            var list = ToList();
            Name = list[1];
            BoxName = list[2];
            SetMap(BoxName);
            if (!int.TryParse(list[3], out PortNumber)) throw new Exception("IOConfGeiger: wrong port number: " + row);
            
        }
    }
}
