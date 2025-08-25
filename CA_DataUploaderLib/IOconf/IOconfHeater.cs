using CA.LoopControlPluginBase;
using System.Linq;
using static CA_DataUploaderLib.HeatingController;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfHeater : IOconfOut230Vac, IIOconfRowWithDecision
    {
        public IOconfHeater(string row, int lineNum) : base(row, lineNum, "Heater")
        {
            //note: the format used to be "Heater;Name;BoxName;port number;MaxTemperature;CurrentSensingNoiseThreshold",
            //but CurrentSensingNoiseThreshold got removed as its no longer relevant to the current decision logic
            //(used to influence repeat of off commands, but now they run unconditionally by the SwitchboardController.
            Format = "Heater;Name;BoxName;port number;[MaxTemperature]";

            var list = ToList();
            if (list.Count >= 5 && int.TryParse(list[4], out var maxTemperature))
                MaxTemperature = maxTemperature;
        }

        public readonly int? MaxTemperature = null;

        public LoopControlDecision CreateDecision(IIOconf ioconf) => new HeaterDecision(this, ioconf.GetOven().SingleOrDefault(x => x.HeatingElement.Name == Name), ioconf);
    }
}
