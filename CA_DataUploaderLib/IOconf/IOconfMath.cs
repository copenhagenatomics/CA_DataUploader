using System;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfMath : IOconfRow
    {
        public IOconfMath(string row, int lineNum) : base(row, lineNum, "Math")
        {
            var list = ToList();
            if (list[0] != "Math") throw new Exception("IOconfMath: wrong format: " + row);
            

            Name = list[1];
        }

        public string Name { get; set; }
    }
}
