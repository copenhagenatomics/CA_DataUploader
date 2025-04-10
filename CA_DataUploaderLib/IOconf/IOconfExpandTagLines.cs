using System;
using System.Collections.Generic;
using System.Linq;

namespace CA_DataUploaderLib.IOconf
{
    internal sealed class IOconfExpandTagLines : IOconfRow
    {
        public const string ConfigName = "ExpandTagLines";
        private readonly string _tag;
        private readonly string _expression;
        private readonly List<string> expandedLines = [];
        public IOconfExpandTagLines(string row, int lineNum) : base(row, lineNum, ConfigName, false, false)
        {
            Format = $"{ConfigName};mytag;row with $name or $matchingtag(tag1 tag2 tag3)";
            var list = ToList();
            if (list.Count < 3 || string.IsNullOrEmpty(list[2]))
                throw new FormatException($"Wrong format: {Row}.{Environment.NewLine}{Format}");
            _tag = list[1];
            _expression = string.Join(';', list.Skip(2));
            Name = $"{ConfigName}{Guid.NewGuid():N}"; //we give it a unique temporary name to avoid duplicate conflicts.
        }

        protected internal override void UseTags(ILookup<string, IOconfRow> rowsByTag)
        {
            if (!rowsByTag.Contains(_tag))
                throw new FormatException($"Tag not found: {_tag}. Row: {Row}");
            expandedLines.AddRange(rowsByTag[_tag].Select(r => ExpandTagExpression(_expression, r)).ToList());
        }
        protected internal override IEnumerable<string> GetExpandedConfRows() => expandedLines;
    }
}
