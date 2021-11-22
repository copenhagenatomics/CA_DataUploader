using System.Collections.Generic;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfRPiTemp : IOconfInput
    {
        public static IOconfRPiTemp Default { get; } = new IOconfRPiTemp("RPiTemp;RPiTemp", 0);
        public bool Disabled;
        public IOconfRPiTemp(string row, int lineNum) : base(row, lineNum, "RPiTemp", false, false, null)
        {
            format = "RPiTemp;Name;[Disabled]";
            var list = ToList();
            Disabled = list.Count > 2 && list[2] == "Disabled";
            Skip = true;
        }

        public IEnumerable<IOconfInput> GetDistributedExpandedInputConf()
        { 
            if (Disabled) yield break;
            foreach (var node in IOconfFile.GetEntries<IOconfNode>())
            {
                //note there is no map entry for the IOconfRpiTemp as it is grabbed it is not an external box, but at the moment we only expose the IOconfNode through it
                var map = Map = new IOconfMap("Map;RpiFakeBox;{Name}_{node.Name}Box;", LineNumber);
                yield return new IOconfInput($"RPiTemp;{Name}_{node.Name}Gpu", LineNumber, Type, false, false, BoardSettings.Default) { Map = map, Skip = true };
                yield return new IOconfInput($"RPiTemp;{Name}_{node.Name}Cpu", LineNumber, Type, false, false, BoardSettings.Default) { Map = map, Skip = true };
            }
        }

        public static bool IsLocalCpuSensor(IOconfInput input) => input.Map.DistributedNode.IsCurrentSystem && input.Name.EndsWith("Cpu");
        public static bool IsLocalGpuSensor(IOconfInput input) => input.Map.DistributedNode.IsCurrentSystem && input.Name.EndsWith("Gpu");
    }
}
