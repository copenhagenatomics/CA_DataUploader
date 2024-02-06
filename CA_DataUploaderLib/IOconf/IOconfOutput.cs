using System;
using System.Collections.Generic;
using System.Linq;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfOutput : IOconfRow
    {
        private readonly BoardSettings _boardSettings;
        private IOconfMap? _map;

        public IOconfOutput(string row, int lineNum, string type, bool parsePort, BoardSettings settings) : base(row, lineNum, type) 
        { 
            Format = $"{type};Name;BoxName;[port number]";
            var list = ToList();
            BoxName = list[2];
            _boardSettings = settings;
            if (parsePort && !int.TryParse(list[3], out PortNumber)) throw new Exception($"{type}: wrong port number: {row}");
            if (PortNumber < 1) throw new Exception($"{type}: port numbers must start at 1 {row}");
        }

        public virtual IEnumerable<IOconfInput> GetExpandedInputConf() => Enumerable.Empty<IOconfInput>();

        public override void ValidateDependencies(IIOconf ioconf)
        {
            Map = GetMap(ioconf, BoxName, _boardSettings);
        }

        public string BoxName { get; }
        public readonly int PortNumber = 1;
        public IOconfMap Map
        {
            get => _map ?? throw new Exception($"Call {nameof(ValidateDependencies)} before accessing {nameof(Map)}.");
            private set => _map = value;
        }

        protected static IOconfMap GetMap(IIOconf ioconf, string boxName, BoardSettings settings)
        {
            var maps = ioconf.GetMap();
            var map = maps.SingleOrDefault(x => x.BoxName == boxName);
            if (map == null)
                throw new Exception($"{boxName} not found in map: {string.Join(", ", maps.Select(x => x.BoxName))}");
            map.BoardSettings = settings;
            return map;
        }

        protected IOconfInput NewPortInput(string name, int portNumber, bool upload = true) => new(Row, LineNumber, Type, Map, portNumber) { Name = name, Upload = upload };
    }
}
