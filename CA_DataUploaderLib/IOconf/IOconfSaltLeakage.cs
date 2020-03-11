using System;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfSaltLeakage : IOconfInput
    {
        public IOconfSaltLeakage(string row) : base(row, "SaltLeakage")
        {
            var list = ToList();
            Name = list[1];
            BoxName = list[2];
            SetMap(BoxName);
            if (!int.TryParse(list[3], out PortNumber)) throw new Exception("IOconfTypeK: wrong port number: " + row);
            if (PortNumber < 0 || PortNumber > 16) throw new Exception("IOconfTypeK: invalid port number: " + row);

        }

    }
}
