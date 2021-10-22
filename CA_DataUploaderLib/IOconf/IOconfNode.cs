using System;
using System.Net;

namespace CA_DataUploaderLib.IOconf
{
    /// <remarks>
    /// When used in a host that supports distributed deployments, the <see cref="IsCurrentSystem"/> property must be set to true on the node corresponding to the host process/computer.
    /// This allows the subsystems (including device detection) to know which boards must be physically connected to this system.
    /// </remarks>
    public class IOconfNode: IOconfRow
    {
        public IOconfNode(string row, int lineNum) : base(row, lineNum, "Node")
        {
            format = "Node;Name;ipaddress:port";
            var list = ToList();
            if (list.Count < 3)
                throw new Exception($"IOconfNode: wrong format: {row} {format}");
            Name = list[1];
            if (!IPEndPoint.TryParse(list[2], out var endPoint))
                throw new Exception($"IOconfNode: failed to parse the passed ip address. format: {row} {format}");
            EndPoint = endPoint;
        }

        public string Name { get; }
        public IPEndPoint EndPoint { get; }
        public bool IsCurrentSystem { get; set; }
    }
}
