using System;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfSaltLeakage : IOconfInput
    {
        public IOconfSaltLeakage(string row, int lineNum) : base(row, lineNum, "SaltLeakage")
        {
            if (PortNumber < 2 || PortNumber > 17) throw new Exception("IOconfSaltLeakage: invalid port number: " + row);
        }

    }
}
