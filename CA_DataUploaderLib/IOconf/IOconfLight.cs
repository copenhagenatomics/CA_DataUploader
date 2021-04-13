namespace CA_DataUploaderLib.IOconf
{
    public class IOconfLight : IOconfOut230Vac
    {
        public IOconfLight(string row, int lineNum) : base(row, lineNum, "Light") { }
    }
}
