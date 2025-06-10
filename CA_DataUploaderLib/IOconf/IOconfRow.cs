#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace CA_DataUploaderLib.IOconf
{
    public partial class IOconfRow
    {
        private string _name = string.Empty;
        private readonly List<string> _parsedList;//while ideally we would not keep these lists around, the alternative with the current design is to reparse the row every time we need it.

        public IOconfRow(string row, int lineNum, string type, bool isUnknown = false, bool requireName = true)
        {
            Row = row;
            LineNumber = lineNum;
            IsUnknown = isUnknown;
            var list = _parsedList = RowWithoutComment().Trim().TrimEnd(';').Split(';')
                .Select(x => x.Trim())
                .ToList();
            if (list[^1].StartsWith("tags:"))
            {
                Tags.AddRange(list[^1][5..]
                    .Split(' ', StringSplitOptions.TrimEntries)
                    .Select(ParseTag));
                list.RemoveAt(list.Count - 1);
            }

            if (!isUnknown && list[0] != type) throw new FormatException("IOconfRow: wrong format: " + row);
            Type = list[0];
            if (requireName)
                Name = list.Count >= 2 // could be overwritten elsewhere. 
                ? list[1]
                : throw new FormatException("IOconfRow: missing Name");
            ExpandsTags = isUnknown; //We do this so that plugin fields lists (related to Code lines) are expanded e.g. Code;myplugin;1.0.0 \n myplugin;mylistfield;tagfields:mytag

            static (string name, string value) ParseTag(string tag)
            {
                var pos = tag.IndexOf('=');
                if (pos < 0)
                    return (tag, tag);
                if (pos == 0)
                    throw new FormatException($"Invalid tag format: {tag}. Expected format: name=value.");
                return (tag[..pos], tag[(pos + 1)..]);
            }
        }

        /// <summary>
        /// Derived classes can set this to true to enable list expansions for that type of configuration row.
        /// </summary>
        public bool ExpandsTags { get; protected init; }
        public string Row { get; }
        public string Type { get; }
        /// <summary>
        /// 1-based line number in the configuration.
        /// </summary>
        public int LineNumber { get; }
        public string Name 
        { 
            get => _name; 
            init
            {
                ValidateName(value);
                _name = value;
            }
        }
        protected string Format { get; init; } = string.Empty;
        public bool IsUnknown { get; }
        public List<(string name, string value)> Tags { get; } = [];

        public List<string> ToList() => _parsedList;

        public virtual string UniqueKey() => Type + Name.ToLower();

        protected string RowWithoutComment()
        {
            var temp = Row;
            var pos = Row.IndexOf("//");
            if (pos > 2 && !Row.Contains("https://") && !Row.Contains("http://"))
                temp = Row[..pos];  // remove any comments in the end of the line. 
            return temp;
        }

        public override string ToString()
        {
            return Type.PadRight(20) + " " + Name;
        }

        /// <summary>
        /// This method is called after the whole IOconf file has been loaded. 
        /// Can be used for verification/initialization of things which would be premature to do during construction.
        /// </summary>
        public virtual void ValidateDependencies(IIOconf ioconf) {}

        protected virtual void ValidateName(string name)
        {
            if (!ValidateNameRegex.IsMatch(name))
                throw new FormatException($"Invalid name: {name}. Name can only contain letters, numbers (except as the first character) and underscore.");
        }

        protected static readonly Regex ValidateNameRegex = NameRegex();
        [GeneratedRegex(@"^[a-zA-Z_]+[a-zA-Z0-9_]*$")]
        private static partial Regex NameRegex();
        [GeneratedRegex(@"\bexpandtag{\s*(?<tag>[^,}\s]+)\s*(?:,\s*separator:(?<separator>[^,}]+))?(?:,(?<expression>[^}]+))?(?<closing>}|$)")]
        private static partial Regex ExpandTagRegex();
        [GeneratedRegex(@"\$matchingtag\((?:\s*(?<tag>[^)\s]+))*\s*\)")]
        private static partial Regex MatchingTagRegex();
        [GeneratedRegex(@"\$tagvalue\(\s*(?<tag>[^)\s]+)\s*\)")]
        private static partial Regex TagValueRegex();

        /// <summary>
        /// Returns the list of expanded vector field names from this class.
        /// </summary>
        public virtual IEnumerable<string> GetExpandedSensorNames() => [];

        /// <summary>
        /// Returns the full list of expanded vector field names from this class and it's associated decisions.
        /// </summary>
        public virtual IEnumerable<string> GetExpandedNames(IIOconf ioconf) 
        {
            foreach (var name in GetExpandedSensorNames())
                yield return name;
            if (this is IIOconfRowWithDecision entryWithDecision)
            {
                foreach (var pluginField in entryWithDecision.CreateDecision(ioconf).PluginFields)
                    yield return pluginField.Name;
            }
        }

        protected virtual internal void UseTags(ILookup<string, IOconfRow> rowsByTag)
        {
            if (!ExpandsTags)
                return;

            for (int i = 0; i < _parsedList.Count; i++)
            {
                var needsSplitBySemicolon = false;
                var missingClosing = false;
                var newValue = ExpandTagRegex().Replace(_parsedList[i], match =>
                {
                    if (match.Groups["closing"].Value != "}")
                    {
                        missingClosing = true;
                        return match.Value;//no change as we need more data
                    }

                    var tag = match.Groups["tag"].Value;
                    var separatorGroup = match.Groups["separator"];
                    var separator = separatorGroup.Success ? separatorGroup.Value : ";";
                    needsSplitBySemicolon |= separator == ";";
                    var expressionGroup = match.Groups["expression"];
                    var expression = expressionGroup.Success ? expressionGroup.Value : "$name";
                    if (!rowsByTag.Contains(tag))
                        throw new FormatException($"Tag not found: {tag}. Row: {Row}");
                    return string.Join(
                        separator,
                        rowsByTag[tag].Select(r => ExpandTagExpression(expression, r)).ToList());
                });
                if (missingClosing && i + 1 >= _parsedList.Count)
                    throw new FormatException($"Missing closing }} for expandtag. Row: {Row}");
                if (missingClosing)
                {//assuming the expandtag has ; in it, so we merge the next entry after the ; and try again.
                    _parsedList[i] = _parsedList[i] + ';' + _parsedList[i + 1];
                    _parsedList.RemoveAt(i + 1);
                    i--;
                    continue;
                }

                if (!needsSplitBySemicolon)
                    _parsedList[i] = newValue;
                else
                {
                    _parsedList.RemoveAt(i);
                    var outputsList = newValue.Split(';').ToList();
                    _parsedList.InsertRange(i, outputsList);
                    i += outputsList.Count - 1;
                }
            }
        }

        protected string ExpandTagExpression(string expression, IOconfRow row) => 
            TagValueRegex().Replace(
                MatchingTagRegex().Replace(
                    expression.Replace("$name", row.Name), 
                    m => row.Tags
                        .FirstOrDefault(t => m.Groups["tag"].Captures.Any(c => t.name == c.Value)) 
                        is var tag && tag != default
                            ? tag.name
                            : throw new FormatException($"matchingtag not found. Row: {Row}")),
                m => (row.Tags
                    .FirstOrDefault(t => m.Groups["tag"].Captures.Any(c => t.name == c.Value)) 
                    is var tag && tag != default 
                        ? tag.value 
                        : throw new FormatException($"tagvalue not found. Row: {Row}")
                    ));

        protected internal virtual IEnumerable<string> GetExpandedConfRows() => [];
    }
}
