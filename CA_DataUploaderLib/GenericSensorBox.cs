using CA_DataUploaderLib.IOconf;
using System.Linq;

namespace CA_DataUploaderLib
{
    public class GenericSensorBox : BaseSensorBox
    {
        public GenericSensorBox(IIOconf ioconf, CommandHandler cmd) : base(cmd, "Generic", ioconf.GetGeneric()) 
        {
            var outputs = ioconf.GetGenericOutputs();
            foreach (var output in outputs.Where(o => o.Map.IsLocalBoard && o.Map.McuBoard != null))
                RegisterBoardWriteActions(
                    output.Map.McuBoard!, output, output.DefaultValue, output.TargetFields, (_, v) => output.GetCommand(v), output.RepeatMilliseconds);
        }
    }
}