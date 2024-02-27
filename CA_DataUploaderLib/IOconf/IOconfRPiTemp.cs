using System.Collections.Generic;
using System.Linq;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfRPiTemp : IOconfRow
    {
        public static IOconfRPiTemp Default { get; } = new IOconfRPiTemp("RPiTemp; RPiTemp", 0);
        public readonly bool Disabled;
        public IOconfRPiTemp(string row, int lineNum) : base(row, lineNum, "RPiTemp")
        {
            Format = "RPiTemp;Name;[Disabled]";
            var list = ToList();
            Disabled = list.Count > 2 && list[2] == "Disabled";
        }

        public IEnumerable<IOconfInput> GetDistributedExpandedInputConf(IIOconf ioconf)
        { 
            if (Disabled) yield break;
            //note there is no map entry for the IOconfRpiTemp as it is not an external box, but at the moment we only expose the IOconfNode through it
            var nodes = ioconf.GetEntries<IOconfNode>().ToList();
            if (nodes.Count == 0)
            {
                var map = new IOconfMap($"Map;RpiFakeBox;{Name}Box", LineNumber);
                map.ValidateDependencies(ioconf); // This is automatically called on regular map entries when the configuration is loaded, but has to be explicitly called in this case.
                yield return NewPortInput($"{Name}Gpu", map, 1);
                yield return NewPortInput($"{Name}Cpu", map, 2);
                yield break;
            }

            foreach (var node in nodes)
            {
                //note there is no map entry for the IOconfRpiTemp as it not an external box, but at the moment we only expose the IOconfNode through it
                var nodeNameClean = node.Name.Replace("-", "");
                var map = new IOconfMap($"Map;RpiFakeBox;{Name}_{nodeNameClean}Box;{node.Name}", LineNumber);
                map.ValidateDependencies(ioconf); // This is automatically called on regular map entries when the configuration is loaded, but has to be explicitly called in this case.
                yield return NewPortInput($"{Name}_{nodeNameClean}Gpu", map, 1);
                yield return NewPortInput($"{Name}_{nodeNameClean}Cpu", map, 2);
            }
        }

        public override IEnumerable<string> GetExpandedNames(IIOconf ioconf)
        {
            foreach (var input in GetDistributedExpandedInputConf(ioconf))
                yield return input.Name;
        }

        private IOconfInput NewPortInput(string name, IOconfMap map, int portNumber) => new(Row, LineNumber, Type, map, portNumber) { Name = name, Skip = true };
        public static bool IsLocalCpuSensor(IOconfInput input) => input.Map.IsLocalBoard == true && input.Name.EndsWith("Cpu");
        public static bool IsLocalGpuSensor(IOconfInput input) => input.Map.IsLocalBoard == true && input.Name.EndsWith("Gpu");
    }
}
