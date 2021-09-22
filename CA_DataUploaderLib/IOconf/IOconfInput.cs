﻿using System;
using System.Linq;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfInput : IOconfRow
    {
        public IOconfInput(string row, int lineNum, string type) : this(row, lineNum, type, true, true, null)  { }
        public IOconfInput(string row, int lineNum, string type, bool parsePortRequired, bool parseBoxName, BoardSettings boardSettings) : base(row, lineNum, type) 
        {
            format = $"{type};Name;BoxName;[port number]";
            var list = ToList();
            Name = list[1];
            
            if (parsePortRequired && list.Count < 4) 
                throw new Exception($"{type}: wrong port number: {row}");
            if (list.Count > 3)
            {
                if (HasPort = int.TryParse(list[3], out var port))
                    PortNumber = port; // we don't do out PortNumber above to avoid it being set to 0 when TryParse returns false.
                else if (parsePortRequired)
                    throw new Exception($"{type}: wrong port number: {row}");
                if ("skip".Equals(list[3], StringComparison.InvariantCultureIgnoreCase)) 
                    Skip = true;
            }
            if (PortNumber < 1) throw new Exception($"{type}: port numbers must start at 1 {row}");

            if (parseBoxName) 
            {
                BoxName = list[2];
                BoardStateSensorName = BoxName + "_state"; // this must match the state sensor names returned by BaseSensorBox
                SetMap(BoxName, boardSettings, Skip) ;
            }
        }

        public virtual bool IsSpecialDisconnectValue(double value) => false;

        public string Name { get; set; }
        public string BoxName { get; set; }
        /// <summary>the 1-based port number</summary>
        public string BoardStateSensorName { get; } 
        public int PortNumber = 1;
        public bool Skip { get; set; }
        public IOconfMap Map { get; set; }
        protected bool HasPort { get; }
        public string SubsystemOverride { get; set; }

        private void SetMap(string boxName, BoardSettings settings, bool skipBoardSettings)
        {
            var maps = IOconfFile.GetMap();
            Map = maps.SingleOrDefault(x => x.BoxName == boxName);
            if (Map == null) 
                throw new Exception($"{boxName} not found in map: {string.Join(", ", maps.Select(x => x.BoxName))}");

            // Map.BoardSettings == BoardSettings.Default is there since some boards need separate board settings, but have multiple sensor entries. 
            // This check means a new BoardSettings instance will be created with first entry of board, but not updated (i.e. shared) among the rest of the board entries.    
            if (!skipBoardSettings && Map.BoardSettings == BoardSettings.Default)
                Map.BoardSettings = settings;
        }
    }
}
