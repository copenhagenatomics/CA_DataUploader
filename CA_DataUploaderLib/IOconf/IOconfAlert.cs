using System;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfAlert : IOconfRow
    {
        public IOconfAlert(string row, int lineNum) : base(row, lineNum, "Alert")
        {
            var list = ToList();
            if (list[0] != "Alert") throw new Exception("IOconfAlert: wrong format: " + row);
            

            Name = list[1];
        }


        public string Name { get; set; }
    }
}
