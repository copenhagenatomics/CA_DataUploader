using System;
using System.Collections.Generic;
using System.Linq;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfRow
    {
        public IOconfRow(string row, int lineNum, string type)
        {
            Row = row;
            Type = type;
            LineNumber = lineNum;
            var list = ToList();
            if (list[0] != Type) throw new Exception("IOconfRow: wrong format: " + Row);
        }

        protected string Row;
        protected string Type;
        protected int LineNumber;

        public List<string> ToList()
        {
            return RowWithoutComment().Split(";".ToCharArray()).Select(x => x.Trim()).ToList();
        }

        public string UniqueKey()
        {
            var list = ToList();
            if(GetType() == typeof(IOconfOven))
                return list[0] + list[2] + list[3];  // you could argue that this should somehow include 1 too. 

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
