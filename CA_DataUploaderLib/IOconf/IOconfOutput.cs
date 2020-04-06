
using System;
using System.Collections.Generic;
using System.Linq;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfOutput : IOconfRow
    {
        public IOconfOutput(string row, int lineNum, string type) : base(row, lineNum, type) { }

        public string Name { get; set; }
        public string BoxName { get; set; }
        public int PortNumber;
        public IOconfMap Map { get; set; }

        protected void SetMap(string boxName)
        {
            var maps = IOconfFile.GetMap();
            Map = maps.SingleOrDefault(x => x.BoxName == boxName);
            if (Map == null)
                throw new Exception($"{boxName} not found in map: {string.Join(", ", maps.Select(x => x.BoxName))}");
        }

    }
}
