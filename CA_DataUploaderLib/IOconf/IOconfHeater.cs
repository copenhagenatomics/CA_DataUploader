using System;
using System.Collections.Generic;
using System.Linq;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfHeater : IOconfOut230Vac
    {
        public IOconfHeater(string row, IEnumerable<IOconfMap> map) : base(row, "Heater", map)
        {

        }
    }
}
