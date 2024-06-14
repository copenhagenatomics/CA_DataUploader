using CA_DataUploaderLib.IOconf;
using Microsoft.VisualStudio.TestTools.UnitTesting;
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

        [TestMethod]
        public void RejectsExpressionWithThousandsSeparator()
        {
            var ex = Assert.ThrowsException<Exception>(() => new IOconfMath("Math;MyName;MyValue + 123,222", 0));
            Assert.AreEqual("IOconfMath: wrong format - expression: Math;MyName;MyValue + 123,222", ex.Message);
        }

        [DataRow("Math;MyName;MyValue + 123", "MyValue")]
        [DataRow("Math;MyName;2 / MyValue", "MyValue")]
        [DataRow("Math;MyName;Sin(MyValue*PI/180)", "MyValue", "PI")]//note PI is not predefined so it is also an argument
        [DataRow("Math;MyName;Round(MyValue % 1, 4)", "MyValue")]
        [DataRow("Math;MyName;Round(Abs(MyValue % 1), 4)", "MyValue")]
        [DataRow("Math;MyName;Max(MyValue, 124)", "MyValue")]
        [DataRow("Math;MyName;Truncate(MyValue)", "MyValue")]
        [DataRow("Math;MyName;if(MyValue > 120,MyValue,2)", "MyValue")]
        [DataRow("Math;MyName;Abs(MyValue % 1)", "MyValue")]
        [DataRow("Math;MyName;Sqrt(MyValue)", "MyValue")]
        [DataRow("Math;MyName;10/(MyValue - 1)", "MyValue")]//because GetSources replaces MyValue with 1 while processing the expression, this test case checks a division by 0 does not prevent getting sources
        [DataRow("Math;MyName;1")]
        [DataRow("Math;MyName;Left", "Left")]
        [DataRow("Math;MyName;Left && Right", "Left", "Right")]
        [DataRow("Math;MyName;Left || Right", "Left", "Right")]
        [DataRow("Math;MyName;Left || Middle && Right", "Left", "Middle", "Right")]
        [DataRow("Math;MyName;(Left && Middle) || (Middle || Left) && (Right || Middle && Left)", "Left", "Middle", "Right")]
        [DataRow("Math;MyName;(Left > 5) || (Middle == 42) && (Right <= 12)", "Left", "Middle", "Right")]
        [DataTestMethod]
        public void CanParseSources(string row, params string[] expectedSources) 
        {
            var math = new IOconfMath(row, 0);
            CollectionAssert.AreEqual(expectedSources, math.SourceNames);
        }

        [TestMethod]
        public void CanParseLinesWithComments()
        {
            var math = new IOconfMath("Math;MyName;2 / MyValue   // This is a comment", 0);
            Assert.AreEqual(0.5d, math.Calculate(new Dictionary<string, object> { { "MyValue", 4 } }));
        }

        [DataRow("Math; MyName; Max()")]
        [DataRow("Math; MyName; Max(val1)")]
        [DataRow("Math; MyName; Max(val1, val2, val3)")]
        [DataRow("Math; MyName; Max(123)")]
        [DataRow("Math; MyName; Max(123, 234, 345)")]
        [DataRow("Math; MyName; Min(123)")]
        [DataRow("Math; MyName; Min(123, 234, 345)")]
        [DataRow("Math; MyName; Abs(123, 234)")]
        [DataRow("Math; MyName; Round(123, 234, 345)")]
        [DataRow("Math; MyName; Sin(123, 234, 345)")]
        [DataRow("Math; MyName; Sqrt()")]
        [DataRow("Math; MyName; Sqrt(123, 234)")]
        [DataTestMethod]
        public void RejectsExpressionIfIncorrectNumberOfArgumentsGivenToBuiltInFunction(string row)
        {
            var ex = Assert.ThrowsException<Exception>(() => new IOconfMath(row, 0));
            Assert.IsTrue(ex.Message.StartsWith("IOconfMath: wrong format - expression:"));
        }

        [TestMethod]
        public void IntegerOverflowCheckAtConstruction()
        {
            Assert.ThrowsException<OverflowException>(() => new IOconfMath("Math; overflowTest; 298*200*200*200*decimalNumber", 0));
        }

        [TestMethod]
        public void IntegerUnderflowCheckAtConstruction()
        {
            Assert.ThrowsException<OverflowException>(() => new IOconfMath("Math; overflowTest; -298*200*200*200*decimalNumber", 0));
        }
    }
}
