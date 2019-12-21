using System;
using System.Collections.Generic;
using System.Linq;

namespace CA_DataUploaderLib.IOconf
{
   public class IOconfValve : IOconfOut230Vac
    {
        public IOconfValve(string row, IEnumerable<IOconfMap> map) : base(row, "Valve", map) {}
    }
}
