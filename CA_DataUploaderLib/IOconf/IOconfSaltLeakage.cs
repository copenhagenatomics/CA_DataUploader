using System;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfSaltLeakage : IOconfInput
    {
        public IOconfSaltLeakage(string row, int lineNum) : base(row, lineNum, "SaltLeakage")
        {
            var list = ToList();
            Name = list[1];
            BoxName = list[2];
            SetMap(BoxName);
            if (!int.TryParse(list[3], out PortNumber)) throw new Exception("IOconfSaltLeakage: wrong port number: " + row);
            if (PortNumber < 1 || PortNumber > 16) throw new Exception("IOconfSaltLeakage: invalid port number: " + row);
        }

    }
}
