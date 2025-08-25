#nullable enable
using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfMap : IOconfRow
    {
        private readonly string? _distributedNodeName;
        private IOconfNode? _distributedNode;
        private readonly Regex ValidateMapNameRegex = new(".*"); // Accepts anything - is replaced in the constructor before Name is updated.

        public IOconfMap(string row, int lineNum) : base(row, lineNum, "Map")
        {
            Format = "Map;SerialNo/COM1/USB1-1.1;BoxName;[NodeName];[baud rate];[customwrites]";

            var list = ToList();
            if (list[0] != "Map") throw new FormatException($"IOconfMap: wrong format: {row} {Format}");
            var isVirtualPort = IsVirtualPortName(list[1]);
            bool isWindows = RpiVersion.IsWindows();
            if (isWindows && list[1].StartsWith("COM"))
                USBPort = list[1];
            else if (!isWindows && list[1].StartsWith("USB"))
                USBPort = "/dev/" + list[1];
            else if (isVirtualPort)
                USBPort = list[1];
            else
                SerialNumber = list[1];

            ValidateMapNameRegex = ValidateNameRegex;
            Name = BoxName = list[2];
            _boardSettings = isVirtualPort ? DefaultVirtualBoardSettings : BoardSettings.Default;

            var customWritesIndex = list.IndexOf("customwrites");
            CustomWritesEnabled = customWritesIndex > -1;
            if (CustomWritesEnabled)
                list.RemoveAt(customWritesIndex);//remove it from the list wherever it was for easier parsing below

            //parsing from here assumes customwrites is not in the list i.e. Map;SerialNo/COM1/USB1-1.1;BoxName;[NodeName];[baud rate]
            if (list.Count <= 3)
                return;

            _distributedNodeName = list.Count == 5 ? list[3] : default;
            var baudrate = 0;
            if (list.Count >= 5 && !int.TryParse(list[4], out baudrate))
                CALog.LogErrorAndConsoleLn(LogID.A, $"Failed to parse the baud rate for the board: {BoxName}. Attempting with defaults.");
            else if (list.Count == 4 && int.TryParse(list[3], out baudrate))
                BaudRate = baudrate;
            else
                _distributedNodeName = list[3];

            BaudRate = baudrate;
        }

        public override void ValidateDependencies(IIOconf ioconf) 
        {
            DistributedNode = _distributedNodeName != default 
                ? ioconf.GetEntries<IOconfNode>().SingleOrDefault(n => n.Name == _distributedNodeName) ?? throw new FormatException($"Failed to find node in configuration for Map: {Row}. Format: {Format}")
                : !ioconf.GetEntries<IOconfNode>().Any() ? IOconfNode.GetSingleNode(ioconf) : throw new FormatException($"The node name is not optional for distributed deployments: {Row}. Format: {Format}");
        }

        public event EventHandler<EventArgs>? OnBoardDetected;
        public bool Setboard(Board board)
        {
            if (!IsLocalBoard)
                return false; //when using a distributed deployment, the map entries are only valid in the specified node.

            if ((board.SerialNumber == SerialNumber && SerialNumber != null) || board.PortName == USBPort)
            {
                Board = board;
                if (board is MCUBoard mcuBoard)
                    McuBoard = mcuBoard;

                OnBoardDetected?.Invoke(this, EventArgs.Empty);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Force the board to be disconnected - e.g. in case of misconfiguration.
        /// </summary>
        public async Task ForceDisconnectBoard()
        {
            if (McuBoard is not null)
            {
                await McuBoard.SafeClose(CancellationToken.None);
                CALog.LogInfoAndConsoleLn(LogID.A, $"Disconnected {McuBoard.BoxName}.");
            }
            Board = null;
            McuBoard = null;
        }

        public string? USBPort { get; }
        private string? SerialNumber { get; }
        public string BoxName { get; }
        public BoardSettings BoardSettings
        {
            get => _boardSettings; 
            set
            {
                _boardSettings = value ?? BoardSettings.Default;
                if (BaudRate == 0) BaudRate = _boardSettings.DefaultBaudRate;
            }
        }
        /// <summary>the baud rate as specified in configuration and otherwise 0</summary>
        /// <remarks>check <see cref="BoardSettings" /> for additional baud rate set by configurations</remarks>
        public int BaudRate { get; private set; }
        /// <summary>the cluster node that is directly connected to the device or <c>default</c> when using </summary>
        public IOconfNode DistributedNode 
        { 
            get => _distributedNode ?? throw new InvalidOperationException($"Call {nameof(ValidateDependencies)} before accessing {nameof(DistributedNode)}.");
            private set => _distributedNode = value;
        }
        public bool IsLocalBoard => DistributedNode.IsCurrentSystem;
        public bool CustomWritesEnabled { get; }
        public Board? Board { get; private set; }
        public MCUBoard? McuBoard { get; private set; }

        private BoardSettings _boardSettings;
        internal static bool IsVirtualPortName(string portName) => portName.StartsWith("vports/");
        public static readonly BoardSettings DefaultVirtualBoardSettings = new()
        {
            SkipBoardAutoDetection = true, //everything about how we detect board data is specific to our units, so this must be skipped for vports
            //reconnect does not play well with socat based ports, so we let it go a full hour without data
            //we should greatly reduce it if we find a way of running socat that plays well with the way reconnects run
            MaxMillisecondsWithoutNewValues = 3600000, 
            //this was randomly set to 10 seconds we needed in our current vports use,
            //but we might introduce a setting in the future for this e.g. a MapSettings line that allows changing these times.
            MillisecondsBetweenReads = 10000,
            SecondsBetweenReopens = 10
        };

        public override string ToString() => $"{BoxName} - {USBPort ?? SerialNumber}";

        protected override void ValidateName(string name) 
        {
            if (!ValidateMapNameRegex.IsMatch(name))
                throw new FormatException($"Invalid map name: {name}. Name can only contain letters, numbers (except as the first character) and underscore.");
        }
    }
}
