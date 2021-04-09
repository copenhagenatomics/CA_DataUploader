using System;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfPressure : IOconfInput
    { 
        public IOconfPressure(string row, int lineNum) : base(row, lineNum, "Pressure", false, true, null)
        {
            format = "Pressure;Name;BoxName;[port number / skip]";
            var list = ToList();
            if ("skip".Equals(list[3].ToLower(), StringComparison.InvariantCultureIgnoreCase))
                Skip = true;
            else if (!int.TryParse(list[3], out PortNumber)) 
                throw new Exception("IOConfPressure: wrong port number: " + row);
        }
    }
}
