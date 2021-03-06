﻿
using System;
using System.Linq;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfOutput : IOconfRow
    {
        public IOconfOutput(string row, int lineNum, string type, bool parsePort = true, BoardSettings settings = null) : base(row, lineNum, type) 
        { 
            format = $"{type};Name;BoxName;[port number]";
            var list = ToList();
            Name = list[1];
            BoxName = list[2];
            SetMap(BoxName, settings); 
            if (parsePort && !int.TryParse(list[3], out PortNumber)) throw new Exception($"{type}: wrong port number: {row}");
            if (PortNumber < 1) throw new Exception($"{type}: port numbers must start at 1 {row}");
        }

        public string Name { get; set; }
        public string BoxName { get; set; }
        public int PortNumber = 1;
        public IOconfMap Map { get; set; }

        protected void SetMap(string boxName, BoardSettings settings)
        {
            var maps = IOconfFile.GetMap();
            Map = maps.SingleOrDefault(x => x.BoxName == boxName);
            if (Map == null)
                throw new Exception($"{boxName} not found in map: {string.Join(", ", maps.Select(x => x.BoxName))}");
            Map.BoardSettings = settings;
        }
    }
}
