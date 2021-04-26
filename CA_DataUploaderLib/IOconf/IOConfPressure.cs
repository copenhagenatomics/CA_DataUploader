using System;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfPressure : IOconfInput
    { 
        public IOconfPressure(string row, int lineNum) : base(row, lineNum, "Pressure", false, true, null)
        {
            format = "Pressure;Name;BoxName;[port number / skip]";
            var list = ToList();
            if (!Skip && !HasPort)
                throw new Exception("IOConfPressure: wrong port number: " + row);
        }
    }
}
