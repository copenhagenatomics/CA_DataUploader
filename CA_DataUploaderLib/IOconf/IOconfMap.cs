using System;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfMap : IOconfRow
    {
        public IOconfMap(string row, int lineNum) : base(row, lineNum, "Map")
        {
            format = "Map;SerialNo/COM1/USB1;BoxName;[baud rate]";

            var list = ToList();
            if (list[0] != "Map") throw new Exception($"IOconfMap: wrong format: {row} {format}");
            if (RpiVersion.IsWindows())
            {
                if (list[1].StartsWith("COM"))
                    USBPort = list[1];
            }
            else
            {
                if (list[1].StartsWith("USB"))
                    USBPort = "/dev/" + list[1];
            }
            if(USBPort == null)
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

        private string USBPort { get; set; }
        private string SerialNumber { get; set; }
        public string BoxName { get; set; }
        public int BaudRate;
        public MCUBoard Board;
    }
}
