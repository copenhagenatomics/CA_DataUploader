using System;
using System.Linq;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfOutput : IOconfRow
    {
        public IOconfOutput(string row, int lineNum, string type, bool parsePort, BoardSettings settings) : base(row, lineNum, type) 
        { 
            Format = $"{type};Name;BoxName;[port number]";
            var list = ToList();
            BoxName = list[2];
            Map = GetMap(BoxName, settings); 
            if (parsePort && !int.TryParse(list[3], out PortNumber)) throw new Exception($"{type}: wrong port number: {row}");
            if (PortNumber < 1) throw new Exception($"{type}: port numbers must start at 1 {row}");
        }

        public string BoxName { get; }
        public readonly int PortNumber = 1;
        public IOconfMap Map { get; private set; }

        protected static IOconfMap GetMap(string boxName, BoardSettings settings)
        {
            var maps = IOconfFile.GetMap();
            var map = maps.SingleOrDefault(x => x.BoxName == boxName);
            if (map == null)
                throw new Exception($"{boxName} not found in map: {string.Join(", ", maps.Select(x => x.BoxName))}");
            map.BoardSettings = settings;
            return map;
        }

        protected IOconfInput NewPortInput(string name, int portNumber) => new(Row, LineNumber, Type, Map, portNumber) { Name = name };
    }
}
