namespace CA_DataUploaderLib.IOconf
{
    public class IOconfScale : IOconfInput
    { 
        public IOconfScale(string row, int lineNum) : base(row, lineNum, "Scale", false, true, 9600) { }
    }
}
