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
            return RowWithoutComment().Split(";".ToCharArray()).Select(x => x.Trim()).ToList();
        }

        public string UniqueKey()
        {
            var list = ToList();
            if(GetType() == typeof(IOconfOven))
                return list[0] + list[2];

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
