using CA_DataUploaderLib.IOconf;

namespace CA_DataUploaderLib
{
    public class Board
    {
        public Board(string portname, IOconfMap? map, string? productType = null)
        {
            PortName = portname;
            Map = map;
            if (map != null)
            {
                map.SetBoard(this);
                BoxName = map.BoxName;
            }
            ProductType = productType;
        }

        public string PortName { get; }
        public IOconfMap? Map { get; private set; }
        public string? ProductType { get; protected set; }
        public string? SerialNumber { get; protected set; }
        public string? SubProductType { get; protected set; }
        public string? McuFamily { get; protected set; }
        public string? SoftwareVersion { get; protected set; }
        public string? SoftwareCompileDate { get; protected set; }
        public string? PcbVersion { get; protected set; }
        public string? BoxName { get; protected set; }
        public string? GitSha { get; protected set; }
        public string? Calibration { get; protected set; }
        public string? UpdatedCalibration { get; protected set; }
        public string? HeaderLines { get; protected set; }

        public bool TrySetMap(IOconfMap map)
        {
            var boardMatched = map.SetBoard(this);
            if (boardMatched)
            {
                Map = map;
                BoxName = map.BoxName;
            }
            return boardMatched;
        }

        public override string ToString() => $"Product Type: {ProductType,-20} Serial Number: {SerialNumber,-12} Port name: {PortName,-18} PCB version: {PcbVersion}";
        public string ToShortDescription() => $"{BoxName} {ProductType} {SerialNumber} {PortName}";
    }
}