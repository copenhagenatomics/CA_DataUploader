﻿using System;
using System.Linq;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfInput : IOconfRow
    {
        public IOconfInput(string row, int lineNum, string type) : this(row, lineNum, type, true, true, null)  { }
        public IOconfInput(string row, int lineNum, string type, bool parsePort, bool parseBoxName, BoardSettings boardSettings) : base(row, lineNum, type) 
        {
            format = $"{type};Name;BoxName;[port number]";
            var list = ToList();
            Name = list[1];
            
            if (parseBoxName) 
            {
                BoxName = list[2];
                SetMap(BoxName, boardSettings);
            }

            if (parsePort && !int.TryParse(list[3], out PortNumber)) throw new Exception($"{type}: wrong port number: {row}");
        }


        public string Name { get; set; }
        public string BoxName { get; set; }
        public int PortNumber;
        public bool Skip { get; set; }
        public IOconfMap Map { get; set; }


        private void SetMap(string boxName, BoardSettings settings)
        {
            var maps = IOconfFile.GetMap();
            Map = maps.SingleOrDefault(x => x.BoxName == boxName);
            if (Map == null) 
                throw new Exception($"{boxName} not found in map: {string.Join(", ", maps.Select(x => x.BoxName))}");
            Map.BoardSettings = settings;
        }
    }
}
