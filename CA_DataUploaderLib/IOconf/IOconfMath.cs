using NCalc;
using System;
using System.Collections.Generic;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfMath : IOconfRow
    {
        public IOconfMath(string row, int lineNum) : base(row, lineNum, "Math")
        {
            var list = ToList();
            if (list[0] != "Math") throw new Exception("IOconfMath: wrong format: " + row);
            
            Name = list[1];
            expression = new Expression(row.Substring(Name.Length + 6));
        }

        public string Name { get; set; }
        private Expression expression;

        public double Calculate(Dictionary<string, object> values)
        {
            expression.Parameters = values;
            return (double)expression.Evaluate();
        }
    }
}
