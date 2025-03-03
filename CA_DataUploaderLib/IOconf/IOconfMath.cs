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
        /// <summary>gets the sources (other vector values) listed explicitly in the math expression</summary>
        /// <remarks>
        /// Note the sources may include the math expression itself, which is the expression referring to its value in the previous cycle.
        /// Also note that the sources returned may also refer to the math expression.
        /// </remarks>
        public List<string> SourceNames { get; }
        public IOconfMath(string row, int lineNum) : base(row, lineNum, "Math")
        {
            Format = "Math;Name;math expression";

            try
            {
                (compiledExpression, SourceNames) = CompileExpression(ToList()[2]);
                // Perform test calculation using default input values
                Convert.ToDouble(Calculate(SourceNames.ToDictionary(s => s, s => (object)0), compiledExpression));
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

        public static (LogicalExpression expression, List<string> sources) CompileExpression(string expression)
        {
            var compiledExpression = Expression.Compile(expression, true);
            var sourceNames = GetSources(compiledExpression);
            return (compiledExpression, sourceNames);
        }

        public override IEnumerable<string> GetExpandedSensorNames()
        {
            yield return Name;
        }

        private readonly LogicalExpression compiledExpression;

        // https://www.codeproject.com/Articles/18880/State-of-the-Art-Expression-Evaluation
        // uses NCalc 2 for .NET core. 
        // https://github.com/sklose/NCalc2
        // examples:
        // https://github.com/sklose/NCalc2/blob/master/test/NCalc.Tests/Fixtures.cs
        // Convert.ToDouble allows some expressions that return int, decimals or even boolean to work
        // note that some expression may even return different values depending on the branch hit i.e. when using if(...)
        public double Calculate(Dictionary<string, object> values)
        {
            try
            {
                return Convert.ToDouble(Calculate(values, compiledExpression));
            }
            catch
            {
                return double.NaN;
            }
        }

        public static bool CalculateBoolean(Dictionary<string, object> values, LogicalExpression compiledExpression) => Convert.ToBoolean(Calculate(values, compiledExpression));
        public static object Calculate(Dictionary<string, object> values, LogicalExpression compiledExpression)
        {
            var expression = new Expression(compiledExpression, EvaluateOptions.OverflowProtection);
            expression.Parameters = values;
            return expression.Evaluate();
        }

        private static List<string> GetSources(LogicalExpression compiledExpression) 
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
