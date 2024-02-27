#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfRow
    {
        private string _name = string.Empty;

        public IOconfRow(string row, int lineNum, string type, bool isUnknown = false)
        {
            Row = row;
            LineNumber = lineNum;
            IsUnknown = isUnknown;
            var list = ToList();
            if (!isUnknown && list[0] != type) throw new Exception("IOconfRow: wrong format: " + row);
            Type = list[0];
            Name = list[1]; // could be overwritten elsewhere. 
        }

        public string Row { get; }
        public string Type { get; }
        /// <summary>
        /// 0-based line number in the configuration.
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

        public List<string> ToList() => RowWithoutComment().Trim().TrimEnd(';').Split(";".ToCharArray()).Select(x => x.Trim()).ToList();

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

        protected static readonly Regex ValidateNameRegex = new(@"^[a-zA-Z_]+[a-zA-Z0-9_]*$");

        /// <summary>
        /// Returns the full list of expanded vector field names from this class and it's associated decisions.
        /// </summary>
        public virtual IEnumerable<string> GetExpandedNames(IIOconf ioconf) 
        { 
            if (this is IIOconfRowWithDecision entryWithDecision)
            {
                foreach (var pluginField in entryWithDecision.CreateDecision(ioconf).PluginFields)
                    yield return pluginField.Name;
            }
        }
    }
}
