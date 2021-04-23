namespace CA_DataUploaderLib.IOconf
{
    public class IOconfMotor : IOconfOutput
    {
        public string Direction;

        public IOconfMotor(string row, int lineNum) : base(row, lineNum, "Motor", false, 
            new BoardSettings() { DefaultBaudRate = 38400, SkipBoardAutoDetection = true })
        {
            format = "Motor;Name;BoxName;Forward/Backward";
            Direction = ToList()[3];
            CALog.LogInfoAndConsoleLn(LogID.A, $"VFD config: disabled type detection for board {Map}");
        }
    }
}
