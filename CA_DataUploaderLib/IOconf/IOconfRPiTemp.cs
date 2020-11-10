using System;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfRPiTemp : IOconfInput
    {
        public IOconfRPiTemp(string row, int lineNum) : base(row, lineNum, "RPiTemp")
        {
            format = "RPiTemp;Name";

            var list = ToList();
            Name = list[1];
            Skip = true;
        }

    }
}
