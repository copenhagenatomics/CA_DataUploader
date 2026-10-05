using CA.LoopControlPluginBase;
using CA_DataUploaderLib;
using CA_DataUploaderLib.IOconf;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System.Collections.Generic;
using System.Text;
using System.Threading.Channels;
using static UnitTests.FullDecisionTestContext;

namespace UnitTests
{
    [TestClass]
    public class DefaultCommandRunnerTests
    {
        [TestMethod]
        public void Execute_RandomCommand_Rejected()
        {
            // Arrange
            var logger = new ChannelLogger();
            var logs = logger.Log;
            var cmd = new CommandHandler(new Mock<IIOconf>().Object, runCommandLoop: false, logger: logger);
            cmd.AddDecisions([new TestDecision([])]);

            // Act
            cmd.Execute("random", true);

            // Assert
            Assert.Contains("unknown command", GetAllLogs(logs));
        }

        [DataRow("test")]
        [DataRow("test 42")]
        [DataRow("test 42.0")]
        [DataRow("test 0x123")]
        [DataRow("test #ffffff")]
        [TestMethod]
        public void Execute_SingleWordCommand_AcceptedWithOrWithoutParameter(string command)
        {
            // Arrange
            var logger = new ChannelLogger();
            var logs = logger.Log;
            var cmd = new CommandHandler(new Mock<IIOconf>().Object, runCommandLoop: false, logger: logger);
            cmd.AddDecisions([new TestDecision(["test"])]);

            // Act
            cmd.Execute(command, true);

            // Assert
            Assert.Contains("command accepted", GetAllLogs(logs));
        }

        [DataRow("er autoscan start", true, false)]
        [DataRow("er_autoscan_start", true, false)]
        [DataRow("er autoscan start 123", true, false)]
        [DataRow("er_autoscan_start 123", true, false)]
        [DataRow("er autoscan star", false, false)]
        [DataRow("er_autoscan_star", false, false)]
        [DataRow("er autoscan star 123", false, false)]
        [DataRow("er_autoscan_star 123", false, false)]
        [DataRow("er autoscan start more", false, false)]
        [DataRow("er_autoscan_start_more", false, false)]
        [DataRow("er autoscan start more 123", false, false)]
        [DataRow("er_autoscan_start_more_123", false, false)]
        [DataRow("er autoscan", false, false)]
        [DataRow("er_autoscan", false, false)]
        [DataRow("er autoscan 123", false, false)]
        [DataRow("er_autoscan 123", false, false)]
        [DataRow("er", false, false)]
        [DataRow("er 123", false, false)]
        [DataRow("ergo", false, true)]
        [DataRow("ergo 123", false, true)]
        [TestMethod]
        public void Execute_MultiWordCommand_Tests(string command, bool accepted, bool unknown)
        {
            // Arrange
            var logger = new ChannelLogger();
            var logs = logger.Log;
            var cmd = new CommandHandler(new Mock<IIOconf>().Object, runCommandLoop: false, logger: logger);
            cmd.AddDecisions([new TestDecision(["er autoscan start"])]);

            // Act
            cmd.Execute(command, true);

            // Assert
            Assert.Contains(accepted
                ? "command accepted"
                : unknown
                    ? "unknown command"
                    : "bad command", GetAllLogs(logs));
        }


        [DataRow("board1", "custom board1 status")]
        [DataRow("board_1", "custom board_1 status")]
        [DataRow("board_room_1", "custom board_room_1 status")]
        [DataRow("board1", "custom board1 set_mode 1")]
        [DataRow("board1", "reconnect board1")]
        [DataRow("board_1", "reconnect board_1")]
        [DataRow("board_room_1", "reconnect board_room_1")]
        [TestMethod]
        public void Execute_BoardCommand_PreservesUnderscores(string boardName, string command)
        {
            // Arrange: use the real board-command validation without connecting hardware.
            var ioconf = new Mock<IIOconf>();
            ioconf.Setup(x => x.GetLoopName()).Returns("TestLoop");
            var map = new IOconfMap($"Map; 1234567890; {boardName}; customwrites", 1);
            map.ValidateDependencies(ioconf.Object);
            var input = new IOconfInput($"Sensor; sensor1; {boardName}; 1", 2, "Sensor", map, 1);
            var logger = new ChannelLogger();
            using var cmd = new CommandHandler(ioconf.Object, runCommandLoop: false, logger: logger);
            _ = new BaseSensorBox(cmd, "sensors", [input]);

            // Act
            cmd.Execute(command, true);

            // Assert: a missing board can report a delivery failure, but the command must validate.
            Assert.Contains($"Command: {command} - command accepted", GetAllLogs(logger.Log));
        }

        public static string GetAllLogs(ChannelReader<string> logs)
        {
            var allLogs = new StringBuilder();
            while (logs.TryRead(out var log))
                allLogs.AppendLine(log);
            return allLogs.ToString();
        }

        private class TestDecision(List<string> handledEvents) : LoopControlDecision
        {
            public override string Name => "TestDecision";
            public override PluginField[] PluginFields => [];
            public override string[] ReferenceFields => [];
            public override string[] HandledEvents => [.. handledEvents];
            public override void Initialize(CA.LoopControlPluginBase.VectorDescription desc) { }
            public override void MakeDecision(CA.LoopControlPluginBase.DataVector vector, List<string> events) { }
            public override void SetConfig(IDecisionConfig config) { }
        }
    }
}
