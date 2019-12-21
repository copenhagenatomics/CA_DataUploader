using System.Collections.Generic;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfHeater : IOconfOut230Vac
    {
        public IOconfHeater(string row, IEnumerable<IOconfMap> map) : base(row, "Heater", map)
        {

        }
    }
}
