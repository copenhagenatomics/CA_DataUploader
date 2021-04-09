namespace CA_DataUploaderLib.IOconf
{
    public class IOconfValve : IOconfOut230Vac
    {
        public IOconfValve(string row, int lineNum) : base(row, lineNum, "Valve") { }
    }
}
