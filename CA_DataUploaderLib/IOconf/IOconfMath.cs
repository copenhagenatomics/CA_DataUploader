#nullable enable
using NCalc;
using NCalc.Domain;
using System;
using System.Collections.Generic;
using System.Linq;

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
                compiledExpression = Expression.Compile(ToList()[2], true);
                SourceNames = GetSources();
                // Perform test calculation using default input values
                Calculate(SourceNames.ToDictionary(s => s, s => (object)0));
            }
            catch (OverflowException ex)
            {
                throw new OverflowException("IOconfMath: expression causes integer overflow: " + row, ex);
            }
            catch (Exception ex)
            {
                throw new Exception("IOconfMath: wrong format - expression: " + row, ex);
            }
        }

        public override IEnumerable<string> GetExpandedSensorNames(IIOconf ioconf)
        {
            yield return Name;
        }

        private readonly LogicalExpression compiledExpression;

        // https://www.codeproject.com/Articles/18880/State-of-the-Art-Expression-Evaluation
        // uses NCalc 2 for .NET core. 
        // https://github.com/sklose/NCalc2
        // examples:
        // https://github.com/sklose/NCalc2/blob/master/test/NCalc.Tests/Fixtures.cs
        public double Calculate(Dictionary<string, object> values)
        {
            var expression = new Expression(compiledExpression, EvaluateOptions.OverflowProtection);
            expression.Parameters = values;
            // Convert.ToDouble allows some expressions that return int, decimals or even boolean to work
            // note that some expression may even return different values depending on the branch hit i.e. when using if(...)
            return Convert.ToDouble(expression.Evaluate());
        }

        private List<string> GetSources() 
        {
            ParameterExtractionVisitor visitor = new();
            compiledExpression.Accept(visitor);
            return [..visitor.Parameters];
        }

        private class ParameterExtractionVisitor : LogicalExpressionVisitor
        {
            public HashSet<string> Parameters = [];

            public override void Visit(Identifier function)
            {
                Parameters.Add(function.Name);
            }

            public override void Visit(UnaryExpression expression)
            {
                expression.Expression.Accept(this);
            }

            public override void Visit(BinaryExpression expression)
            {
                expression.LeftExpression.Accept(this);
                expression.RightExpression.Accept(this);
            }

            public override void Visit(TernaryExpression expression)
            {
                expression.LeftExpression.Accept(this);
                expression.RightExpression.Accept(this);
                expression.MiddleExpression.Accept(this);
            }

            public override void Visit(Function function)
            {
                foreach (var expression in function.Expressions)
                    expression.Accept(this);
            }

            public override void Visit(LogicalExpression expression) 
            {
                throw new InvalidOperationException("Unexpected math expression.");
            }

            public override void Visit(ValueExpression expression) { } //Constant - discard
        }
    }
}
