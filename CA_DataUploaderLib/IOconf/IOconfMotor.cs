using System;
using System.Collections.Generic;
using System.Linq;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfMotor : IOconfOutput
    {
        public IOconfMotor(string row, IEnumerable<IOconfMap> map) : base(row, "Motor")
        {
            var list = ToList();
            Name = list[1];
            BoxName = list[2];
            Map = map.Single(x => x.BoxName == BoxName);
            if (!int.TryParse(list[3], out PortNumber)) throw new Exception("IOconfMotor: wrong port number: " + row);

        }

        public string Name { get; set; }
        public string BoxName { get; set; }
        public int PortNumber;
        public IOconfMap Map { get; set; }
    }
}
