﻿using System;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfMap : IOconfRow
    {
        public IOconfMap(string row, int lineNum) : base(row, lineNum, "Map")
        {
            format = "Map;SerialNo/COM1/USB1;BoxName;[baud rate]";

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
            if (list.Count > 3 && !int.TryParse(list[3], out BaudRate))
                BaudRate = 115200; // default baud rate. 
        }

        public bool SetMCUboard(MCUBoard board)
        {
            if ((board.serialNumber == SerialNumber && SerialNumber != null) || board.PortName == USBPort)
            {
                Board = board;
                return true;
            }

            return false;
        }

        public string USBPort { get; private set; }
        private string SerialNumber { get; set; }
        public string BoxName { get; set; }
        public int BaudRate = 115200;
        public MCUBoard Board;

        public override string ToString() => $"{BoxName} - ${USBPort ?? SerialNumber}";
    }
}
