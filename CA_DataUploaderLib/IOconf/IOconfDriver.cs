﻿
namespace CA_DataUploaderLib.IOconf
{
    public class IOconfDriver : IOconfRow
    {
        public IOconfDriver(string row, int lineNum, string type) : base(row, lineNum, type) { }

        protected override void ValidateName(string name) { } // no validation
    }
}
