using CA_DataUploaderLib.IOconf;
using System.Linq;

namespace CA_DataUploaderLib
{
    public class GenericSensorBox : BaseSensorBox
    {
        public GenericSensorBox(CommandHandler cmd) : base(cmd, "Generic", IOconfFile.GetGeneric()) 
        {
            var outputs = IOconfFile.GetGenericOutputs();
            foreach (var output in outputs.Where(o => o.Map.IsLocalBoard && o.Map.McuBoard != null))
                RegisterBoardWriteActions(
                    output.Map.McuBoard!, output, output.DefaultValue, output.TargetField, (_, v) => output.GetCommand(v));
        }
    }
}