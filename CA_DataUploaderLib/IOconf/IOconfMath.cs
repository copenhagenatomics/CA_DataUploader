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
            inputExpression = row.Substring(Name.Length + 6);
            expression = new Expression(inputExpression);
        }

        public string Name { get; set; }
        public List<string> VarNames;
        private string inputExpression;
        private Expression expression;

        public void SetVarNames(List<string> names)
        {
            names.ForEach(x => { if (inputExpression.Contains(x)) VarNames.Add(x); });
        }

        public double Calculate(Dictionary<string, object> values)
        {
            expression.Parameters = values;
            return (double)expression.Evaluate();
        }
    }
}
