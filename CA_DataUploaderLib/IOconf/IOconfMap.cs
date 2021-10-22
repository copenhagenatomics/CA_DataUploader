﻿using System;
using System.Linq;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfMap : IOconfRow
    {
        public IOconfMap(string row, int lineNum) : base(row, lineNum, "Map")
        {
            format = "Map;SerialNo/COM1/USB1-1.1;BoxName;[NodeName];[baud rate]";

            var list = ToList();
            if (list[0] != "Map") throw new Exception($"IOconfMap: wrong format: {row} {format}");
            bool isWindows = RpiVersion.IsWindows();
            if (isWindows && list[1].StartsWith("COM"))
                USBPort = list[1];
            else if (!isWindows && list[1].StartsWith("USB"))
                USBPort = "/dev/" + list[1];
            else
                SerialNumber = list[1];

            BoxName = list[2];
            if (list.Count <= 3)
                return;

            string distributedNodeName = list.Count == 5 ? list[3] : default;
            var baudrate = 0;
            if (list.Count >= 5 && !int.TryParse(list[4], out baudrate))
                CALog.LogErrorAndConsoleLn(LogID.A, $"Failed to parse the baud rate for the board: {BoxName}. Attempting with defaults.");
            else if (list.Count == 4 && int.TryParse(list[3], out baudrate))
                BaudRate = baudrate;
            else
                distributedNodeName = list[3];

            BaudRate = baudrate;
            DistributedNode = distributedNodeName != default 
                ? IOconfFile.GetEntries<IOconfNode>().SingleOrDefault(n => n.Name == distributedNodeName) ?? throw new Exception($"Failed to find node in configuration for Map: {row}. Format: {format}")
                : !IOconfFile.GetEntries<IOconfNode>().Any() ? default : throw new Exception($"The node name is not optional for distributed deployments: {row}. Format: {format}");
        }

        public event EventHandler<EventArgs> OnBoardDetected;
        public bool SetMCUboard(MCUBoard board)
        {
            if (DistributedNode?.IsCurrentSystem == false)
                return false; //when using a distributed deployment, the map entries are only valid in the specified node.

            if ((board.serialNumber == SerialNumber && SerialNumber != null) || board.PortName == USBPort)
            {
                Board = board;
                OnBoardDetected?.Invoke(this, EventArgs.Empty);
                return true;
            }

            return false;
        }

        public string USBPort { get; private set; }
        private string SerialNumber { get; set; }
        public string BoxName { get; set; }
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
        public IOconfNode DistributedNode { get; }
        public bool IsLocalBoard => DistributedNode == null || DistributedNode.IsCurrentSystem;

        public MCUBoard Board;
        private BoardSettings _boardSettings = BoardSettings.Default;

        public override string ToString() => $"{BoxName} - {USBPort ?? SerialNumber}";
    }
}
