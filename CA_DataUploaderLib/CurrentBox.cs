using CA_DataUploaderLib.IOconf;
using System.Collections.Generic;
using System.Linq;

namespace CA_DataUploaderLib
{
    public class CurrentBox : BaseSensorBox
    {
        public CurrentBox(CommandHandler cmd) : base(cmd, "Current", GetSensorConfigs()) { }

        private static IEnumerable<IOconfInput> GetSensorConfigs()
        {
            return IOconfFile.GetEntries<IOconfCurrent>().Cast<IOconfInput>().Concat(IOconfFile.GetEntries<IOconfCurrentFault>());
        }
    }
}
