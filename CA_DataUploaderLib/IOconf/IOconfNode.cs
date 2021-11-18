using System;
using System.Net;

namespace CA_DataUploaderLib.IOconf
{
    /// <remarks>
    /// When used in a host that supports distributed deployments, the <see cref="IsCurrentSystem"/> property must be set to true on the node corresponding to the host process/computer.
    /// This allows the subsystems (including device detection) to know which boards must be physically connected to this system.
    /// </remarks>
    public class IOconfNode : IOconfRow
    {
        private readonly IPEndPoint _endPoint;

        private static readonly Lazy<IOconfNode> _singleNode = new(() => 
            new IOconfNode(IOconfFile.GetLoopName()) { IsCurrentSystem = true }
        );
        private static byte _nodeInstances;

        public IOconfNode(string row, int lineNum) : base(row, lineNum, "Node")
        {
            format = "Node;Name;ipaddress:port;[role]";
            var list = ToList();
            if (list.Count < 3)
                throw new Exception($"IOconfNode: wrong format: {row} {format}");
            Name = list[1];
            if (!IPEndPoint.TryParse(list[2], out var endPoint))
                throw new Exception($"IOconfNode: failed to parse the passed ip address. format: {row} {format}");
            _endPoint = endPoint;
            NodeIndex = _nodeInstances++;
            if (list.Count > 3)
                Role = list[3];
        }

        private IOconfNode(string name) : base($"Node;{name}", 0, "Node")
        {
            Name = name;
        }

        public static IOconfNode SingleNode => _singleNode.Value;
        public string Name { get; }
        public IPEndPoint EndPoint => _endPoint ?? throw new InvalidOperationException($"Endpoint is only supported when running with distributed configuration");
        public bool IsCurrentSystem { get; set; }
        /// <summary>position of the node in the config (starting at 0)</summary>
        public byte NodeIndex { get; }
        public string Role { get; }
    }
}
