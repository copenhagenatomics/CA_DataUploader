using CA_DataUploaderLib.IOconf;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NCalc;
using System;
using System.Collections.Generic;

namespace UnitTests
{
    [TestClass]
    public class IOconfMathTests
    {
        [DataRow("Math;MyName;MyValue + 123", 1d, 124d)]
        [DataRow("Math;MyName;MyValue + 193.123", 1d, 194.123d)]
        [DataRow("Math;MyName;MyValue - 193.123", 1d, -192.123d)]
        [DataRow("Math;MyName;MyValue / 2", 1d, 0.5d)]
        [DataRow("Math;MyName;2 / MyValue", 4d, 0.5d)]
        [DataRow("Math;MyName;2 / MyValue", 0d, double.PositiveInfinity)]
        [DataRow("Math;MyName;MyValue + 123", double.NaN, double.NaN)]
        [DataRow("Math;MyName;MyValue / 3", 8d, 2.6666666666666666667d)]
        [DataRow("Math;MyName;Sin(MyValue*PI/180)", 90d, 1d)]//note PI is not predefined and passed explicitely in the test body.
        [DataRow("Math;MyName;Round(MyValue % 1, 4)", 123.6542, 0.6542d)]
        [DataRow("Math;MyName;Round(MyValue % 1, 2)", 123.6582, 0.66d)]
        [DataRow("Math;MyName;Round(Abs(MyValue % 1), 4)", 123.6542, 0.6542d)]
        [DataRow("Math;MyName;Max(MyValue, 124)", 123.6542, 124d)]
        [DataRow("Math;MyName;Truncate(MyValue)", 123.6542, 123d)]
        [DataRow("Math;MyName;if(MyValue > 120,MyValue,2)", 123.6542, 123.6542)]
        [DataRow("Math;MyName;if(MyValue > 120,MyValue,2.0)", 113.6542, 2)]
        [DataRow("Math;MyName;if(MyValue > 120,MyValue,2)", 113.6542, 2)]
        [DataRow("Math;MyName;Abs(MyValue % 1)", 123.6542, 0.654200000000003d)]
        [DataRow("Math;MyName;Sqrt(MyValue)", 4, 2)]
        [DataRow("Math;MyName;Sqrt(MyValue)", -1, double.NaN)]
        [DataTestMethod]
        public void CalculatesExpectedValue(string row, double value, double expectedResult) 
        {
            var math = new IOconfMath(row, 0);
            Assert.AreEqual(expectedResult, math.Calculate(new Dictionary<string, object> { { "MyValue", value }, { "PI", Math.PI} }));
        }

        [DataRow("Math;MyName;MyValue > 2", 5d, 1d)]
        [DataRow("Math;MyName;MyValue > 2", -2d, 0d)]
        [DataTestMethod]
        public void CanUseComparisons(string row, double value, double expectedResult)
        {
            var math = new IOconfMath(row, 0);
            Assert.AreEqual(expectedResult, math.Calculate(new Dictionary<string, object> { { "MyValue", value }}));
        }

        [DataTestMethod]
        public void RejectsExpressionWithThousandsSeparator()
        {
            var ex = Assert.ThrowsException<Exception>(() => new IOconfMath("Math;MyName;MyValue + 123,222", 0));
            Assert.AreEqual("IOconfMath: wrong format - expression: Math;MyName;MyValue + 123,222", ex.Message);
        }

    }
}
