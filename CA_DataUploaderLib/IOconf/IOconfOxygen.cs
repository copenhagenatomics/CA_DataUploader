
namespace CA_DataUploaderLib.IOconf
{
    public class IOconfOxygen : IOconfInput
    {
        public IOconfOxygen(string row, int lineNum) : base(row, lineNum, "Oxygen")
        {
            format = "Oxygen;Name;BoxName";

            var list = ToList();
            Name = list[1];
            BoxName = list[2];
            SetMap(BoxName);
        }
        
    }
}