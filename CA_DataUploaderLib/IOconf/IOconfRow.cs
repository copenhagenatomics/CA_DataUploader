#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfRow
    {
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
        public int LineNumber { get; }
        public string Name { get; init; }
        protected string Format { get; init; } = string.Empty;
        public bool IsUnknown { get; }

        public List<string> ToList() => RowWithoutComment().Trim().TrimEnd(';').Split(";".ToCharArray()).Select(x => x.Trim()).ToList();

        public string UniqueKey()
        {
            var list = ToList();
            if(GetType() == typeof(IOconfOven))
                return list[0] + list[2] + list[3];  // you could argue that this should somehow include 1 too. 

            return list[0] + Name;
        }

        private string RowWithoutComment()
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
        internal virtual void ValidateDependencies() {}
    }
}
