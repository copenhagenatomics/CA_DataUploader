using System;
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
            if (list.Count > 3 && !(HasPort = int.TryParse(list[3], out PortNumber)) && parsePortRequired) 
                throw new Exception($"{type}: wrong port number: {row}");
            if (list.Count > 3 && "skip".Equals(list[3], StringComparison.InvariantCultureIgnoreCase)) 
                Skip = true;

            if (parseBoxName) 
            {
                BoxName = list[2];
                SetMap(BoxName, boardSettings, Skip) ;
            }
        }


        public string Name { get; set; }
        public string BoxName { get; set; }
        public int PortNumber;
        public bool Skip { get; set; }
        public IOconfMap Map { get; set; }
        protected bool HasPort { get; }

        private void SetMap(string boxName, BoardSettings settings, bool skipBoardSettings)
        {
            var maps = IOconfFile.GetMap();
            Map = maps.SingleOrDefault(x => x.BoxName == boxName);
            if (Map == null) 
                throw new Exception($"{boxName} not found in map: {string.Join(", ", maps.Select(x => x.BoxName))}");
            if (!skipBoardSettings)
                Map.BoardSettings = settings;
        }
    }
}
