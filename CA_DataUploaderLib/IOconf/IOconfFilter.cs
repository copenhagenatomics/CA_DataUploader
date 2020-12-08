using CA_DataUploaderLib.Helpers;
using CA_DataUploaderLib.Extensions;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfFilter : IOconfRow
    {
        public FilterType filterType;
        public double filterLength;  // in seconds. 
        public string Name { get; set; }

        public List<string> SourceNames;
        public bool HideSource { get; private set; }

        public IOconfFilter(string row, int lineNum) : this(row, lineNum, true)
        {
        }

        public IOconfFilter(string row, int lineNum, bool validateSourceNames) : base(row, lineNum, "Filter")
        {
            format = "Filter;Name;FilterType;FilterLength;SourceNames;[hidesource]";

            var list = ToList();
            Name = list[1];
            if (!Enum.TryParse(list[2], out filterType))
                throw new Exception($"Wrong filter type: {row} {Environment.NewLine}{format}");

            if (!list[3].TryToDouble(out filterLength))
                throw new Exception($"Wrong filter length: {row} {Environment.NewLine}{format}");

            var sources = list.Skip(4).ToList();
            if (sources.Last() == "hidesource")
            {
                HideSource = true;
                sources.Remove("hidesource");
            }

            SourceNames = (validateSourceNames) ? IOconfFile.GetInputs().Select(x => x.Name).Where(sources.Contains).ToList() : sources;
        }
    }
}
