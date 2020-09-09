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

            try
            {
                var compiledExpression = Expression.Compile(row.Substring(Name.Length + 6), true);
                expression = new Expression(compiledExpression);
            }
            catch (Exception ex)
            {
                throw new Exception("IOconfMath: wrong format - expression: " + row, ex);
            }
        }

        public string Name { get; set; }
        private Expression expression;

        // https://www.codeproject.com/Articles/18880/State-of-the-Art-Expression-Evaluation
        // uses NCalc 2 for .NET core. 
        // https://github.com/sklose/NCalc2
        // examples:
        // https://github.com/sklose/NCalc2/blob/master/test/NCalc.Tests/Fixtures.cs
        public double Calculate(Dictionary<string, object> values)
        {
            expression.Parameters = values;
            // Convert.ToDouble allows some expressions that return int, decimals or even boolean to work
            // note that some expression may even return different values depending on the branch hit i.e. when using if(...)
            return Convert.ToDouble(expression.Evaluate());
        }
    }
}
