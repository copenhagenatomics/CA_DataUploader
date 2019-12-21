
namespace CA_DataUploaderLib.IOconf
{
    public class IOconfInput : IOconfRow
    {
        public IOconfInput(string row, string type) : base(row, type) { }

        public string Name { get; set; }
        public string BoxName { get; set; }
        public int PortNumber;
        public IOconfMap Map { get; set; }

    }
}
