namespace CA_DataUploaderLib.IOconf
{
    public class IOconfMotor : IOconfOutput
    {
        public string Direction;

        public IOconfMotor(string row, int lineNum) : base(row, lineNum, "Motor", false)
        {
            format = "Motor;Name;BoxName;Forward/Backward";
            Direction = ToList()[3];
        }
    }
}
