using System;
using System.Collections.Generic;
using System.Linq;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfOven : IOconfDriver
    {
        public IOconfOven(string row, IEnumerable<IOconfMap> map) : base(row, "Oven")
        {
            var list = ToList();
            Name = list[1];
            BoxName = list[2];
            if (!int.TryParse(list[3], out PortNumber)) throw new Exception("IOconfOven: wrong port number: " + row);

        }

        public string Name { get; set; }
        public string BoxName { get; set; }
        public int PortNumber;
    }
}
