#nullable enable
using CA_DataUploaderLib.Helpers;
using CA_DataUploaderLib.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfFilter : IOconfRow
    {
        public readonly FilterType filterType;
        public readonly double filterLength;  // in seconds. 
        public string NameInVector => Name + "_filter";
        public List<string> SourceNames { get; }
        public bool HideSource { get; }

        public IOconfFilter(string row, int lineNum) : base(row, lineNum, "Filter")
        {
            Format = "Filter;Name;FilterType;FilterLength;SourceNames;[hidesource]";

            var list = ToList();
            if (!Enum.TryParse(list[2], out filterType))
                throw new Exception($"Wrong filter type: {row} {Environment.NewLine}{Format}");

            if (!list[3].TryToDouble(out filterLength))
                throw new Exception($"Wrong filter length: {row} {Environment.NewLine}{Format}");

            var sources = list.Skip(4).ToList();
            if (sources.Last() == "hidesource")
            {
                HideSource = true;
                sources.Remove("hidesource");
            }

            // source validation happens later, as there is not a 1-1 relation of IOConfFile entries and values getting into the Vector i.e. some oxygen sensors have 3 values. 
            SourceNames = sources;
        }
    }
}
