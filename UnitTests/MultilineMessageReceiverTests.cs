#nullable enable
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using static CA_DataUploaderLib.BaseSensorBox;

namespace UnitTests
{
    [TestClass]
    public class MultilineMessageReceiverTests
    {
        private static readonly string completeMultilineMessage = """
Start of board Status:
The board is operating normally.
Port 1 measures voltage[0 - 5V]
Port 2 measures voltage[0 - 5V]
Port 3 measures voltage[0 - 5V]
Port 4 measures voltage[0 - 5V]
Port 5 measures voltage[0 - 5V]
Port 6 measures voltage[0 - 5V]

End of board status.
""";

        private static readonly string incompleteMultilineMessage = """
Start of board Status:
The board is operating normally.
Port 1 measures voltage[0 - 5V]
Port 2 measures voltage[0 - 5V]

""";

        private readonly List<string> linesWithCompleteMessage = [
"4.650325, 3543.687988, 4.639473, 4.656060, 4.628024, 4.629683, 0x0",
"4.650254, 3543.561768, 4.639433, 4.655966, 4.627975, 4.629673, 0x0",
..completeMultilineMessage.Split(Environment.NewLine),
"4.650369, 3543.680420, 4.639513, 4.656144, 4.627993, 4.629726, 0x0",
"4.650223, 3543.606201, 4.639447, 4.656077, 4.627962, 4.629629, 0x0" ];

        private readonly List<string> linesWithIncompleteMessage = [
"4.650325, 3543.687988, 4.639473, 4.656060, 4.628024, 4.629683, 0x0",
"4.650254, 3543.561768, 4.639433, 4.655966, 4.627975, 4.629673, 0x0",
..incompleteMultilineMessage.Split(Environment.NewLine),
"4.650369, 3543.680420, 4.639513, 4.656144, 4.627993, 4.629726, 0x0",
"4.650223, 3543.606201, 4.639447, 4.656077, 4.627962, 4.629629, 0x0" ];

        [TestMethod]
        public void HandleLine_CompleteInfoBlock_Logged()
        {
            // Arrange
            var logCount = 0;
            var log = "";
            var sut = new MultilineMessageReceiver((s) => { logCount++; log += s; });

            // Act
            foreach (var line in linesWithCompleteMessage)
                sut.HandleLine(line);

            // Assert
            Assert.AreEqual(1, logCount);
            Assert.IsTrue(log.Contains(completeMultilineMessage));
        }

        [TestMethod]
        public void HandleLine_IncompleteInfoBlock_NothingIsLoggedImmediately()
        {
            // Arrange
            var logCount = 0;
            var log = "";
            var sut = new MultilineMessageReceiver((s) => { logCount++; log += s; });

            // Act
            foreach (var line in linesWithIncompleteMessage)
                sut.HandleLine(line);

            // Assert
            Assert.AreEqual(0, logCount);
        }

        [TestMethod]
        public void HandleLine_IncompleteInfoBlock_LoggedAfterLineCountExceeded()
        {
            // Arrange
            var logCount = 0;
            var log = "";
            var sut = new MultilineMessageReceiver((s) => { logCount++; log += s; });

            // Act
            foreach (var line in linesWithIncompleteMessage)
                sut.HandleLine(line);
            for (var i = 0; i < 123; i++) //Some large number of value lines
                sut.HandleLine("4.650369, 3543.680420, 4.639513, 4.656144, 4.627993, 4.629726, 0x0");

            // Assert
            Assert.AreEqual(1, logCount);
            Assert.IsTrue(log.Contains(incompleteMultilineMessage));
        }

        [TestMethod]
        public void LogPossibleIncompleteMessage_CompleteMessage_NothingIsLogged()
        {
            // Arrange
            var logCount = 0;
            var log = "";
            var sut = new MultilineMessageReceiver((s) => { logCount++; log += s; });
            foreach (var line in linesWithCompleteMessage)
                sut.HandleLine(line);
            logCount = 0;

            // Act
            sut.LogPossibleIncompleteMessage();

            // Assert
            Assert.AreEqual(0, logCount);
        }

        [TestMethod]
        public void LogPossibleIncompleteMessage_IncompleteMessage_Logged()
        {
            // Arrange
            var logCount = 0;
            var log = "";
            var sut = new MultilineMessageReceiver((s) => { logCount++; log += s; });
            foreach (var line in linesWithIncompleteMessage)
                sut.HandleLine(line);

            // Act
            sut.LogPossibleIncompleteMessage();

            // Assert
            Assert.AreEqual(1, logCount);
            Assert.IsTrue(log.Contains(incompleteMultilineMessage));
        }
    }
}
