using System;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfSaltLeakage : IOconfInput
    {
        public IOconfSaltLeakage(string row, int lineNum) : base(row, lineNum, "SaltLeakage")
        {
            if (PortNumber < 1 || PortNumber > 16) throw new Exception("IOconfSaltLeakage: invalid port number: " + row);
        }

    }
}
