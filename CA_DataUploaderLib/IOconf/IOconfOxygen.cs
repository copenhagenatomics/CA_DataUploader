namespace CA_DataUploaderLib.IOconf
{
    public class IOconfOxygen : IOconfInput
    {
        public IOconfOxygen(string row, int lineNum) : base(row, lineNum, "Oxygen", false, true, 9600)
        {
            format = "Oxygen;Name;BoxName";
        }
    }
}