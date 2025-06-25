using CA_DataUploaderLib;
using CA_DataUploaderLib.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace UnitTests
{
    [TestClass]
    public class DefaultCommandRunnerTests
    {
        [TestMethod]
        public void Run_RandomCommand_ReturnsFalse()
        {
            // Arrange
            DefaultCommandRunner runner = new(new Mock<ILog>().Object);

            // Act + Assert
            Assert.IsFalse(runner.Run("random", true));
        }

        [TestMethod]
        public void Run_SingleWorkCommand_ReturnsTrueWithOrWithoutParameter()
        {
            // Arrange
            DefaultCommandRunner runner = new(new Mock<ILog>().Object);
            foreach (var (command, func) in GetValidationCommands(["test"]))
                runner.AddCommand(command, func);

            // Act + Assert
            Assert.IsTrue(runner.Run("test", true));
            Assert.IsTrue(runner.Run("test 42", true));
            Assert.IsTrue(runner.Run("test 42.0", true));
            Assert.IsTrue(runner.Run("test 0x123", true));
            Assert.IsTrue(runner.Run("test #123456", true));
        }

        [TestMethod]
        public void Run_MultiWordCommand_Tests()
        {
            // Arrange
            DefaultCommandRunner runner = new(new Mock<ILog>().Object);
            foreach (var (command, func) in GetValidationCommands(["er autoscan start"]))
                runner.AddCommand(command, func);

            // Act + Assert
            Assert.IsTrue(runner.Run("er autoscan start", true));
            Assert.IsTrue(runner.Run("er autoscan start 123", true));
            Assert.IsFalse(runner.Run("er autoscan start more", true));
            Assert.IsFalse(runner.Run("er autoscan start more123", true));
            Assert.IsFalse(runner.Run("er autoscan", true));
            Assert.IsFalse(runner.Run("er autoscan 123", true));
            Assert.IsFalse(runner.Run("er", true));
            Assert.IsFalse(runner.Run("er 123", true));
            Assert.IsFalse(runner.Run("ergo", true));
            Assert.IsFalse(runner.Run("ergo 123", true));
        }

        /// <summary>
        /// This method is a (almost identical) copy of the one in <see cref="CA_DataUploaderLib.Extensions.DecisionExtensions"/>
        /// </summary>
        public static IEnumerable<(string command, Func<List<string>, bool> commandValidationFunction)> GetValidationCommands(List<string> events)
        {
            foreach (var e in events)
            {
                //This just avoids the commands being reported as rejected for now, but the way to go about in the long run is to add detection of the executed commands by looking at the vectors.
                //Note that even then, the decisions are not reporting which commands they actually handled or ignore, specially as they are receiving all commands and then handle what applies to the decision.
                var expectedArgs = e.Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                var firstWordInEvent = expectedArgs[0];
                yield return (firstWordInEvent, args =>
                {
                    if (args.Count != expectedArgs.Length && args.Count != expectedArgs.Length + 1)
                        return false;

                    for (int i = 1; i < expectedArgs.Length; i++)
                    {
                        if (!args[i].Equals(expectedArgs[i], StringComparison.OrdinalIgnoreCase))
                            return false;
                    }

                    if (args.Count == expectedArgs.Length)
                        return true;

                    var target = args[expectedArgs.Length];
                    return target.TryToDouble(out _) ||
                        target.StartsWith("0x") && uint.TryParse(target[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _) ||
                        target.StartsWith('#') && uint.TryParse(target[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _);
                }
                );
            }
        }
    }
}
