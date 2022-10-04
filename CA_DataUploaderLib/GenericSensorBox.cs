using CA_DataUploaderLib.IOconf;

namespace CA_DataUploaderLib
{
    public class GenericSensorBox : BaseSensorBox
    {
        public GenericSensorBox(CommandHandler cmd) : base(cmd, "Generic", IOconfFile.GetGeneric()) { }
    }
}