using System;
using System.Collections.Generic;
using System.Linq;

namespace CA_DataUploaderLib.IOconf
{
    public sealed class IOconfExpand : IOconfRow
    {
        public const string ConfigName = "Expand";
        public IOconfExpand(string row, int lineNum) : base(row, lineNum, ConfigName, false, false)
        {
            Format = $"{ConfigName};name1;name2;...;namen;-;row with $name{Environment.NewLine}{ConfigName};tagfields:mytag;suffix:mysuffix;row with $name";
            ExpandsTags = true;
            var list = ToList();
            if (list.Count < 3 || string.IsNullOrEmpty(list[2]))
                throw new FormatException($"Wrong format: {Row}.{Environment.NewLine}{Format}");
            Name = $"expand{Guid.NewGuid():N}"; // we name as the whole row, to avoid duplicate conflicts.
        }

        protected internal override IEnumerable<string> GetExpandedConfRows()
        {
            var list = ToList().Skip(1).ToList();
            var endOfList = list.IndexOf("-");
            if (endOfList == -1)
                throw new FormatException($"Wrong format: {Row}. {Format}");
            foreach (var value in list.Take(endOfList))
                yield return string.Join(';', list.Skip(endOfList + 1)).Replace("$name", value);
        }
    }
}
