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
                Tags.AddRange(list[^1][5..].Split(' ').Select(x => x.Trim()));
                list.RemoveAt(list.Count - 1);
            }

            if (!isUnknown && list[0] != type) throw new Exception("IOconfRow: wrong format: " + row);
            Type = list[0];
            if (requireName)
                Name = list.Count >= 2 // could be overwritten elsewhere. 
                ? list[1]
                : throw new Exception("IOconfRow: missing Name");
            ExpandsTags = isUnknown;
        }

        /// <summary>
        /// Derived classes can set this to true to enable list expansions for that type of configuration row.
        /// </summary>
        public bool ExpandsTags { get; protected set; }
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
        public List<string> Tags { get; } = [];

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
                throw new Exception($"Invalid name: {name}. Name can only contain letters, numbers (except as the first character) and underscore.");
        }

        protected static readonly Regex ValidateNameRegex = NameRegex();
        [GeneratedRegex(@"^[a-zA-Z_]+[a-zA-Z0-9_]*$")]
        private static partial Regex NameRegex();

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

        protected internal void UseTags(ILookup<string, string> tags)
        {
            if (!ExpandsTags)
                return;

            for (int i = 0; i < _parsedList.Count; i++)
            {
                if (!_parsedList[i].StartsWith("tagfields:")) continue;
                
                var tag = _parsedList[i][10..];
                _parsedList.RemoveAt(i);
                if (!tags.Contains(tag))
                    throw new FormatException($"Tag not found: {tag}. Row: {Row}");

                var tagFields = tags[tag];
                if (i < _parsedList.Count && _parsedList[i].StartsWith("suffix:"))
                {
                    var suffix = _parsedList[i][7..];
                    _parsedList.RemoveAt(i);
                    tagFields = tagFields.Select(t => t + "_" + suffix);
                }

                if (i + 1 < _parsedList.Count)
                    tagFields = tagFields.Append("-");//add delimiter if the list is not the last value in the line.

                var tagFieldsList = tagFields.ToList();
                _parsedList.InsertRange(i, tagFieldsList);
                i += tagFieldsList.Count - 1;
            }
        }

        protected internal virtual IEnumerable<string> GetExpandedConf() => [];
    }
}
