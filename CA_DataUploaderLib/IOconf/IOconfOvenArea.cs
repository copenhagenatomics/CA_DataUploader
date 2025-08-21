#nullable enable
using System;
using System.Linq;
using CA.LoopControlPluginBase;
using static CA_DataUploaderLib.HeatingController;

namespace CA_DataUploaderLib.IOconf
{
    internal sealed class IOconfOvenArea : IOconfDriver, IIOconfRowWithDecision
    {
        public IOconfOvenArea(string row, int lineNum) : base(row, lineNum, "OvenArea")
        {
            Format = "OvenArea;AreaNumber";

            var list = ToList();
            if (!int.TryParse(list[1], out OvenArea)) 
                throw new FormatException($"Oven area must be a number: {row} {Format}");
            if (OvenArea < 1)
                throw new FormatException("Oven area must be a number bigger than or equal to 1");
        }

        public LoopControlDecision CreateDecision(IIOconf ioconf) => new OvenAreaDecision(new($"ovenarea{OvenArea}", OvenArea, ioconf.GetEntries<IOconfOvenProportionalControlUpdates>().SingleOrDefault()));

        public override string UniqueKey()
        {
            var list = ToList();
            return list[0] + list[1];
        }

        public readonly int OvenArea;
    }
}
