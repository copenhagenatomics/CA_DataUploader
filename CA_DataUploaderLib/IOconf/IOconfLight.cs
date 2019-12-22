using System;
using System.Collections.Generic;
using System.Linq;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfLight : IOconfOut230Vac
    {
        public IOconfLight(string row) : base(row, "Light") { }
    }
}
