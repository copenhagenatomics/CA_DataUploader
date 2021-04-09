namespace CA_DataUploaderLib.IOconf
{
    public class IOconfRPiTemp : IOconfInput
    {
        public static IOconfRPiTemp Default { get; } = new IOconfRPiTemp("RPiTemp;RPiTemp", 0);
        public bool Disabled;
        public IOconfRPiTemp(string row, int lineNum) : base(row, lineNum, "RPiTemp", false, false, null)
        {
            format = "RPiTemp;Name;[Disabled]";
            var list = ToList();
            Disabled = list.Count > 2 && list[2] == "Disabled";
            Skip = true;
        }

        public IOconfRPiTemp WithName(string name) => new IOconfRPiTemp(Row, LineNumber) {Name = name };
    }
}
