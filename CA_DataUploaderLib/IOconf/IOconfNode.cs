using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;

namespace CA_DataUploaderLib.IOconf
{
    /// <remarks>
    /// When used in a host that supports distributed deployments, the <see cref="IsCurrentSystem"/> property must be set to true on the node corresponding to the host process/computer.
    /// This allows the subsystems (including device detection) to know which boards must be physically connected to this system.
    /// </remarks>
    public class IOconfNode : IOconfRow
    {
        private readonly IPEndPoint? _endPoint;
        private static readonly ConditionalWeakTable<IIOconf,IOconfNode> _singleNode = [];
        private static readonly ConditionalWeakTable<IIOconf, object> _nodeInstances = []; //using object to work around reference only limitations for the ConditionalWeakTable value

        public IOconfNode(string row, int lineNum) : base(row, lineNum, "Node")
        {
            Format = "Node;Name;ipaddress:port;[role]";
            var list = ToList();
            if (list.Count < 3)
                throw new Exception($"IOconfNode: wrong format: {row} {Format}");

            if (!IPEndPoint.TryParse(list[2], out var endPoint))
                throw new Exception($"IOconfNode: failed to parse the passed ip address. format: {row} {Format}");
            _endPoint = endPoint;
            if (list.Count > 3)
                Role = list[3];
            IsUploader = Role == "uploader";
        }

        private IOconfNode(string name) : base($"Node;{name}", 0, "Node") { }

        public override void ValidateDependencies(IIOconf ioconf)
        {
            NodeIndex = _nodeInstances.TryGetValue(ioconf, out var instances) ? (byte)instances : (byte)0;
            _nodeInstances.AddOrUpdate(ioconf, (byte)(NodeIndex + 1));
            base.ValidateDependencies(ioconf);
        }

        internal static IOconfNode GetSingleNode(IIOconf conf)
        {
            if (_singleNode.TryGetValue(conf, out var node))
                return node;

            node = new IOconfNode(conf.GetLoopName()) { IsCurrentSystem = true, IsUploader = true };
            _singleNode.Add(conf, node);
            return node;
        }

        public IPEndPoint EndPoint => _endPoint ?? throw new InvalidOperationException($"Endpoint is only supported when running with distributed configuration");
        public bool IsCurrentSystem { get; set; }
        /// <summary>position of the node in the config (starting at 0)</summary>
        public byte NodeIndex { get; private set; }
        public string? Role { get; }
        public bool IsUploader { get; private init; }
        public static bool IsCurrentSystemAnUploader(IReadOnlyCollection<IOconfNode> allNodes) => 
            allNodes.SingleOrDefault(n => n.IsCurrentSystem)?.IsUploader ?? allNodes.Count == 0;

        protected override void ValidateName(string name) { } // no validation
    }
}
