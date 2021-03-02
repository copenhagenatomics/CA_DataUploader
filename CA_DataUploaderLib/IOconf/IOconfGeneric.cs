using System;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfGeneric : IOconfInput
    {
        public IOconfGeneric(string row, int lineNum) : base(row, lineNum, "GenericSensor")
        {
            format = "GenericSensor;Name;BoxName;[port number]";
            var list = ToList();
            Name = list[1];
            BoxName = list[2];
            SetMap(BoxName);
            if (!int.TryParse(list[3], out PortNumber)) throw new Exception("GenericSensor: wrong port number: " + row);
        }
    }
}