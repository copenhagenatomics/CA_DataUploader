#nullable enable
using NCalc;
using NCalc.Domain;
using System;
using System.Collections.Generic;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfMath : IOconfRow
    {
        /// <summary>gets the sources (other vector values) listed explicitely in the math expression</summary>
        /// <remarks>
        /// Note the sources may include the math expression itself, which is the expression refering to its value in the previous cycle.
        /// Also note that the sources returned may also refer to the math expression.
        /// </remarks>
        public List<string> SourceNames { get; }
        public IOconfMath(string row, int lineNum) : base(row, lineNum, "Math")
        {
            Format = "Math;Name;math expression";

            try
            {
                compiledExpression = Expression.Compile(RowWithoutComment()[(Name.Length + 6)..], true);
            }
            catch (Exception ex)
            {
                throw new Exception("IOconfMath: wrong format - expression: " + row, ex);
            }

            SourceNames = GetSources();
        }

        private readonly LogicalExpression compiledExpression;

        // https://www.codeproject.com/Articles/18880/State-of-the-Art-Expression-Evaluation
        // uses NCalc 2 for .NET core. 
        // https://github.com/sklose/NCalc2
        // examples:
        // https://github.com/sklose/NCalc2/blob/master/test/NCalc.Tests/Fixtures.cs
        public double Calculate(Dictionary<string, object> values)
        {
            var expression = new Expression(compiledExpression);
            expression.Parameters = values;
            // Convert.ToDouble allows some expressions that return int, decimals or even boolean to work
            // note that some expression may even return different values depending on the branch hit i.e. when using if(...)
            return Convert.ToDouble(expression.Evaluate());
        }

        private List<string> GetSources() 
        {
            HashSet<string> sources = new();
            var expression = new Expression(compiledExpression);
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
            return new List<string>(sources);

            void EvalFunction(string name, FunctionArgs args) 
            {
                args.EvaluateParameters();
                args.Result = 1;
            };

            void EvalParameter(string name, ParameterArgs args) 
            {
                sources.Add(name);
                args.Result = 1;
            };
        }
    }
}
