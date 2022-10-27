using System;
using System.Collections.Generic;
using System.Linq;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfInput : IOconfRow
    {
        public IOconfInput(string row, int lineNum, string type) : this(row, lineNum, type, true, BoardSettings.Default) { }
        public IOconfInput(string row, int lineNum, string type, bool parsePortRequired, BoardSettings boardSettings) : base(row, lineNum, type)
        {
            Format = $"{type};Name;BoxName;[port number]";
            var list = ToList();
            (HasPort, Skip, PortNumber) = GetPort(row, type, parsePortRequired, list);
            BoxName = list[2];
            BoardStateSensorName = BoxName + "_state"; // this must match the state sensor names returned by BaseSensorBox
            Map = GetMap(BoxName, boardSettings, Skip);
        }

        public IOconfInput(string row, int lineNum, string type, IOconfMap map, int portNumber) : base(row, lineNum, type)
        {
            Format = $"{type};Name;BoxName;[port number]";
            PortNumber = portNumber;
            BoxName = map.BoxName;
            Map = map;
            BoardStateSensorName = BoxName + "_state"; // this must match the state sensor names returned by BaseSensorBox
        }

        public virtual bool IsSpecialDisconnectValue(double value) => false;

        public string BoxName { get; init; }
        /// <summary>the 1-based port number</summary>
        public string BoardStateSensorName { get; }
        public int PortNumber = 1;

        public bool Skip { get; init; }
        public IOconfMap Map { get; }
        protected bool HasPort { get; }
        public string? SubsystemOverride { get; init; }

        private static (bool hasPort, bool skip, int port) GetPort(string row, string type, bool parsePortRequired, List<string> list)
        {
            if (list.Count > 3 && int.TryParse(list[3], out var port))
            {
                if (port < 1) throw new Exception($"{type}: port numbers must start at 1 {row}");
                return (true, false, port);
            }
            else if (parsePortRequired)
                throw new Exception($"{type}: wrong port number: {row}");
            else if (list.Count > 3 && "skip".Equals(list[3], StringComparison.InvariantCultureIgnoreCase))
                return (false, true, 1);
            else
                return (false, false, 1);
        }
        private static IOconfMap GetMap(string boxName, BoardSettings settings, bool skipBoardSettings)
        {
            var maps = IOconfFile.GetMap();
            var map = maps.SingleOrDefault(x => x.BoxName == boxName) ?? 
                throw new Exception($"{boxName} not found in map: {string.Join(", ", maps.Select(x => x.BoxName))}");
            // Map.BoardSettings == BoardSettings.Default is there since some boards need separate board settings, but have multiple sensor entries. 
            // This check means a new BoardSettings instance will be created with first entry of board, but not updated (i.e. shared) among the rest of the board entries.    
            if (!skipBoardSettings && map.BoardSettings == BoardSettings.Default)
                map.BoardSettings = settings;
            return map;
        }

        public class Expandable : IOconfRow
        {
            public Expandable(string row, int lineNum, string type, bool parsePortRequired, BoardSettings boardSettings) : base(row, lineNum, type)
            {
                Format = $"{type};Name;BoxName;[port number]";
                var list = ToList();
                (_, var skip, PortNumber) = GetPort(row, type, parsePortRequired, list);
                if (skip)
                    throw new Exception($"{type}: unexpected skip: {row}");
                Map = GetMap(list[2], boardSettings, false);
            }

            public string BoxName => Map.BoxName;
            /// <summary>the 1-based port number</summary>
            public int PortNumber = 1;
            public IOconfMap Map { get; }

            protected IOconfInput NewInput(string name, int portNumber, string? subsystemOverride = null) => 
                new(Row, LineNumber, Type, Map, portNumber) { Name = name, SubsystemOverride = subsystemOverride };
        }
    }
}
