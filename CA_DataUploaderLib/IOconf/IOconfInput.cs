using System;
using System.Collections.Generic;
using System.Linq;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfInput : IOconfRow
    {
        private readonly BoardSettings _boardSettings = BoardSettings.Default;
        private IOconfMap? _map;

        public IOconfInput(string row, int lineNum, string type) : this(row, lineNum, type, true, BoardSettings.Default) { }
        public IOconfInput(string row, int lineNum, string type, bool parsePortRequired, BoardSettings boardSettings) : base(row, lineNum, type)
        {
            Format = $"{type};Name;BoxName;[port number]";
            var list = ToList();
            (HasPort, Skip, PortNumber) = GetPort(row, type, parsePortRequired, list);
            BoxName = list[2];
            BoardStateSensorName = BoxName + "_state"; // this must match the state sensor names returned by BaseSensorBox
            _boardSettings = boardSettings;
        }

        public IOconfInput(string row, int lineNum, string type, IOconfMap map, int portNumber) : base(row, lineNum, type)
        {
            Format = $"{type};Name;BoxName;[port number]";
            PortNumber = portNumber;
            BoxName = map.BoxName;
            Map = map;
            BoardStateSensorName = BoxName + "_state"; // this must match the state sensor names returned by BaseSensorBox
        }

        public override void ValidateDependencies(IIOconf ioconf)
        {
            if (_map is not null) return;

            Map = GetMap(ioconf, BoxName, _boardSettings, Skip);
        }

        public override IEnumerable<string> GetExpandedSensorNames(IIOconf ioconf)
        {
            yield return Name;
        }

        public virtual bool IsSpecialDisconnectValue(double value) => false;

        public string BoxName { get; init; }
        public string BoardStateSensorName { get; }
        /// <summary>the 1-based port number</summary>
        public int PortNumber = 1;

        public bool Skip { get; init; }
        public IOconfMap Map
        {
            get => _map ?? throw new Exception($"Call {nameof(ValidateDependencies)} before accessing {nameof(Map)}.");
            private set => _map = value;
        }
        protected bool HasPort { get; }
        public string? SubsystemOverride { get; init; }
        public bool Upload { get; init; } = true;

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

        private static IOconfMap GetMap(IIOconf ioconf, string boxName, BoardSettings settings, bool skipBoardSettings)
        {
            var maps = ioconf.GetMap();
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
            private readonly BoardSettings _boardSettings;
            private IOconfMap? _map;
            
            public Expandable(string row, int lineNum, string type, bool parsePortRequired, BoardSettings boardSettings) : base(row, lineNum, type)
            {
                Format = $"{type};Name;BoxName;[port number]";
                var list = ToList();
                (_, var skip, PortNumber) = GetPort(row, type, parsePortRequired, list);
                if (skip)
                    throw new Exception($"{type}: unexpected skip: {row}");
                BoxName = list[2];
                _boardSettings = boardSettings;
            }

            public override void ValidateDependencies(IIOconf ioconf)
            {
                Map = GetMap(ioconf, BoxName, _boardSettings, false);
            }

            public virtual IEnumerable<IOconfInput> GetExpandedConf() => Enumerable.Empty<IOconfInput>();

            public override IEnumerable<string> GetExpandedSensorNames(IIOconf ioconf)
            {
                foreach (var input in GetExpandedConf())
                    yield return input.Name;
                foreach (var name in base.GetExpandedNames(ioconf))
                    yield return name;
            }

            public string BoxName { get; }
            /// <summary>the 1-based port number</summary>
            public int PortNumber = 1;
            
            public IOconfMap Map
            {
                get => _map ?? throw new Exception($"Call {nameof(ValidateDependencies)} before accessing {nameof(Map)}.");
                private set => _map = value;
            }

            protected IOconfInput NewInput(string name, int portNumber, string? subsystemOverride = null) => 
                new(Row, LineNumber, Type, Map, portNumber) { Name = name, SubsystemOverride = subsystemOverride };
        }
    }
}
