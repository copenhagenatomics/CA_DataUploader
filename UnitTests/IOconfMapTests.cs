using CA_DataUploaderLib.IOconf;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;

namespace UnitTests
{
    [TestClass]
    public class IOconfMapTests
    {
        [DataRow("0_cannot_start_with_number")]
        [DataRow("æøå")]
        [DataRow("hat^")]
        [DataRow("pipe|")]
        [DataRow("back_\\slash")]
        [DataRow("forward_/slash")]
        [DataRow("ampersand&")]
        [DataRow("question?")]
        [DataRow("colon:")]
        [DataRow("exclamation!")]
        [DataRow("half½")]
        [DataRow("paragraph§")]
        [DataRow("turtle¤")]
        [DataRow("hash#tag")]
        [DataRow("percent_%rel")]
        [DataRow("angle<bracket>")]
        [DataRow("curly{bracket}")]
        [DataRow("square[bracket]")]
        [DataRow("name with space")]
        [DataRow("name_with(parenthesis)")]
        [DataRow("name~with~tilde")]
        [DataRow("name*with*star")]
        [DataRow("name,with,comma")]
        [DataRow("name.with.dot")]
        [DataRow("name=with=equals")]
        [DataRow("name+with+plus")]
        [DataRow("name-with-dash")]
        [DataTestMethod]
        public void InvalidName(string name)
        {
            var ex = Assert.ThrowsException<Exception>(() => new IOconfMap($"Map; anything_goes_here; {name}", 0));
            Assert.IsTrue(ex.Message.StartsWith($"Invalid map name: {name}"), ex.Message);
        }

        [DataRow("name_with_number_42")]
        [DataRow("UPPERCASE")]
        [DataRow("lowercase")]
        [DataRow("_name_starting_with_underscore")]
        [DataRow("name_with_underscore")]
        [DataTestMethod]
        public void ValidName(string name)
        {
            _ = new IOconfMap($"Map; anything_goes_here; {name}", 0);
        }

        [TestMethod]
        public void ValidateDependencies_WhenNodeNameIsNotSpecifiedForSinglePiSystem_DistributedNodeGetsUpdated()
        {
            // Arrange
            var ioConfMock = new Mock<IIOconf>();
            ioConfMock.Setup(x => x.GetLoopName()).Returns("TestLoop");
            var mapline = new IOconfMap($"Map; 1234567890; MiscBox", 1);

            // Act
            mapline.ValidateDependencies(ioConfMock.Object);

            // Assert
            Assert.AreEqual("TestLoop", mapline.DistributedNode.Name);
        }

        [TestMethod]
        public void ValidateDependencies_WhenNodeNameIsSpecifiedForDistributedSystem_DistributedNodeGetsUpdated()
        {
            // Arrange
            var nodeLine = new IOconfNode("Node; pi42; 192.168.100.42", 0);
            var ioConfMock = new Mock<IIOconf>();
            ioConfMock.Setup(x => x.GetLoopName()).Returns("TestLoop");
            ioConfMock.Setup(x => x.GetEntries<IOconfNode>()).Returns(new[] { nodeLine });
            var mapline = new IOconfMap($"Map; 1234567890; MiscBox; pi42", 1);

            // Act
            mapline.ValidateDependencies(ioConfMock.Object);

            // Assert
            Assert.AreEqual(nodeLine, mapline.DistributedNode);
        }

        [TestMethod]
        public void ValidateDependencies_WhenNodeNamePointsToSomethingNonExistent_AnExceptionIsThrown()
        {
            // Arrange
            var ioConfMock = new Mock<IIOconf>();
            ioConfMock.Setup(x => x.GetLoopName()).Returns("TestLoop");
            ioConfMock.Setup(x => x.GetEntries<IOconfNode>()).Returns(new[] { new IOconfNode("Node; pi42; 192.168.100.42", 0) });
            var mapline = new IOconfMap($"Map; 1234567890; MiscBox; pi42incorrect", 1);

            // Act + Assert
            var ex = Assert.ThrowsException<FormatException>(() => mapline.ValidateDependencies(ioConfMock.Object));
            Assert.IsTrue(ex.Message.StartsWith($"Failed to find node in configuration for Map"), ex.Message);

        }

        [TestMethod]
        public void ValidateDependencies_WhenNodeNameIsNotSpecifiedForDistributedSystem_AnExceptionIsThrown()
        {
            // Arrange
            var ioConfMock = new Mock<IIOconf>();
            ioConfMock.Setup(x => x.GetEntries<IOconfNode>()).Returns(new[] { new IOconfNode("Node; pi42; 192.168.100.42", 0) });
            var mapline = new IOconfMap($"Map; 1234567890; MiscBox", 1);

            // Act + Assert
            var ex = Assert.ThrowsException<FormatException>(() => mapline.ValidateDependencies(ioConfMock.Object));
            Assert.IsTrue(ex.Message.StartsWith($"The node name is not optional for distributed deployments"), ex.Message);
        }
    }
}
