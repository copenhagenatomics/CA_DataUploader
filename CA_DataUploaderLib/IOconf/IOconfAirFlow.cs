namespace CA_DataUploaderLib.IOconf
{
    public class IOconfAirFlow : IOconfInput
    {
        public IOconfAirFlow(string row, int lineNum) : 
            base(row, lineNum, "AirFlow", true, true, new BoardSettings() { ExpectedHeaderLines = 12 }) { }
    }
}