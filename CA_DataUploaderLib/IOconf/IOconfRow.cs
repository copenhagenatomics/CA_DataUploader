using System;
using System.Collections.Generic;
using System.Linq;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfRow
    {
        public IOconfRow(string row, string type)
        {
            Row = row;
            Type = type;
            var list = ToList();
            if (list[0] != Type) throw new Exception("IOconfRow: wrong format: " + Row);
        }

        protected string Row;
        protected string Type; 

        public List<string> ToList()
        {
            return RowWithoutComment().Split(";".ToCharArray()).ToList();
        }

        public string UniqueKey()
        {
            var list = ToList();
            return list[0] + list[1];
        }

        private string RowWithoutComment()
        {
            var temp = Row;
            var pos = Row.IndexOf("//");
            if (pos > 2 && !Row.Contains("https://"))
                temp = Row.Substring(0, pos);  // remove any comments in the end of the line. 
            return temp;
        }
    }
}
