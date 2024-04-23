using CA_DataUploaderLib.IOconf;
using System.Collections.Generic;
using System.Linq;

namespace CA_DataUploaderLib
{
    public class CurrentBox : BaseSensorBox
    {
        public CurrentBox(IIOconf ioconf, CommandHandler cmd) : base(cmd, "Current", GetSensorConfigs(ioconf)) { }

        private static IEnumerable<IOconfInput> GetSensorConfigs(IIOconf ioconf)
        {
            return ioconf.GetEntries<IOconfCurrent>().Cast<IOconfInput>().Concat(ioconf.GetEntries<IOconfCurrentFault>());
        }
    }
}
