using NCalc;
using System;
using System.Collections.Generic;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfMath : IOconfInput
    {
        public IOconfMath(string row, int lineNum) : base(row, lineNum, "Math", false, false, null)
        {
            format = "Math;Name;math expression";

            var list = ToList();

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

        private readonly Expression expression;

        // https://www.codeproject.com/Articles/18880/State-of-the-Art-Expression-Evaluation
        // uses NCalc 2 for .NET core. 
        // https://github.com/sklose/NCalc2
        // examples:
        // https://github.com/sklose/NCalc2/blob/master/test/NCalc.Tests/Fixtures.cs
        public SensorSample Calculate(Dictionary<string, object> values)
        {
            expression.Parameters = values;
            // Convert.ToDouble allows some expressions that return int, decimals or even boolean to work
            // note that some expression may even return different values depending on the branch hit i.e. when using if(...)
            return new SensorSample(this, Convert.ToDouble(expression.Evaluate())) { TimeStamp = DateTime.UtcNow };
        }

        public List<string> GetSources() 
        {
            HashSet<string> parameters = new HashSet<string>();
            expression.EvaluateFunction += EvalFunction; 
            expression.EvaluateParameter += EvalParameter;
            try
            {
                expression.Evaluate();              
            }
            finally
            {
                expression.EvaluateFunction -= EvalFunction; 
                expression.EvaluateParameter -= EvalParameter;
            }
            return new List<string>(parameters);

            void EvalFunction(string name, NCalc.FunctionArgs args) 
            {
                args.EvaluateParameters();
                args.Result = 1;
            };

            void EvalParameter(string name, NCalc.ParameterArgs args) 
            {
                parameters.Add(name);
                args.Result = 1;
            };
        }
    }
}
