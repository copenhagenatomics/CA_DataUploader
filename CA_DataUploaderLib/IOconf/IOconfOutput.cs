using System;
using System.Collections.Generic;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfOutput : IOconfRow, IIOconfRowWithBoardState
    {
        private readonly BoardSettings _boardSettings;
        private IOconfMap? _map;

        public IOconfOutput(string row, int lineNum, string type, bool parsePort, BoardSettings settings) : base(row, lineNum, type) 
        { 
            Format = $"{type};Name;BoxName;[port number]";
            var list = ToList();
            BoxName = list[2];
            BoardStateName = BaseSensorBox.GetBoxStateName(BoxName);
            _boardSettings = settings;
            if (parsePort && !int.TryParse(list[3], out PortNumber)) throw new FormatException($"{type}: wrong port number: {row}");
            if (PortNumber < 1) throw new FormatException($"{type}: port numbers must start at 1 {row}");
        }

        public virtual IEnumerable<IOconfInput> GetExpandedInputConf()
        {
            var portNumber = PortNumber;
            foreach (var input in GetExpandedSensorNames())
                yield return NewPortInput(input, portNumber++);
        }

        public override void ValidateDependencies(IIOconf ioconf)
        {
            Map = GetMap(ioconf, BoxName, _boardSettings);
        }

        public string BoxName { get; }
        public string BoardStateName { get; }

        public readonly int PortNumber = 1;
        public IOconfMap Map
        {
            get => _map ?? throw new InvalidOperationException($"Call {nameof(ValidateDependencies)} before accessing {nameof(Map)}.");
            private set => _map = value;
        }

        protected IOconfInput NewPortInput(string name, int portNumber, bool upload = true) => new(Row, LineNumber, Type, Map, portNumber) { Name = name, Upload = upload };
    }
}
