using System;
using System.Collections.Generic;
using System.Linq;

namespace CA_DataUploaderLib.IOconf
{
    internal sealed class IOconfExpandLines : IOconfRow
    {
        public const string ConfigName = "Expandlines";
        private readonly List<string> _values;
        private readonly string _expression;
        public IOconfExpandLines(string row, int lineNum) : base(row, lineNum, ConfigName, false, false)
        {
            Format = $"{ConfigName};name1,name2,...;namen;row with $name";
            var list = ToList();
            if (list.Count < 3 || string.IsNullOrEmpty(list[2]))
                throw new FormatException($"Wrong format: {Row}.{Environment.NewLine}{Format}");
            _values = [.. list[1].Split(',')];
            _expression = string.Join(';', list.Skip(2));
            Name = $"Expandlines{Guid.NewGuid():N}"; //we give it a unique temporary name to avoid duplicate conflicts.
        }

        protected internal override IEnumerable<string> GetExpandedConfRows() => _values.Select(value => _expression.Replace("$name", value));
    }
}
