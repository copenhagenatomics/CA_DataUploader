using CA_DataUploaderLib.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfFilter : IOconfRow
    {
        public FilterType filterType;
        public double filterLength;  // in seconds. 
        public string Name { get; set; }

        public List<IOconfInput> SourceNames;


        public IOconfFilter(string row, int lineNum) : base(row, lineNum, "Filter")
        {
            format = "Filter;Name;FilterType;FilterLength;SourceNames";

            var list = ToList();
            Name = list[1];
            if (!Enum.TryParse(list[2], out filterType))
                throw new Exception($"Wrong filter type: {row} {Environment.NewLine}{format}");

            if (!double.TryParse(list[3], out filterLength))
                throw new Exception($"Wrong filter length: {row} {Environment.NewLine}{format}");

            SourceNames = IOconfFile.GetInputs().Where(x => list.Skip(4).Contains(x.Name)).ToList();
        }
    }
}
