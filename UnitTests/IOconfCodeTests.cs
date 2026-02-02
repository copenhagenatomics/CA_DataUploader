using CA_DataUploaderLib.IOconf;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;

namespace UnitTests
{

    [TestClass]
    public class IOconfCodeTests
    {
        [TestMethod]
        public void ValidateDependencies_WithoutCodeRepo_NoExceptionIsThrown()
        {
            // Arrange
            var ioConfMock = new Mock<IIOconf>();
            ioConfMock.Setup(x => x.GetLoopName()).Returns("TestLoop");
            var sut = new IOconfCode($"Code; somePlugin; 0.123.0; pc1", 1);

            // Act
            sut.ValidateDependencies(ioConfMock.Object);

            // Assert
            Assert.AreEqual("somePlugin", sut.ClassName);
            Assert.AreEqual("pc1", sut.Name);
            Assert.AreSame(IOconfCodeRepo.Default, sut.CodeRepo);
        }

        [TestMethod]
        public void ValidateDependencies_WhenMatchingCodeRepoIsNotConfigured_ThrowsException()
        {
            // Arrange
            var ioConfMock = new Mock<IIOconf>();
            ioConfMock.Setup(x => x.GetLoopName()).Returns("TestLoop");
            var sut = new IOconfCode($"Code; salt/somePlugin; 0.123.0; pc1", 1);

            // Act + Assert
            var ex = Assert.Throws<FormatException>(() => sut.ValidateDependencies(ioConfMock.Object));
            Assert.Contains("CodeRepo with name 'salt' not found for Code line", ex.Message);
        }

        [TestMethod]
        public void ValidateDependencies_WhenNoMatchingCodeRepoIsConfigured_ThrowsException()
        {
            // Arrange
            var ioConfMock = new Mock<IIOconf>();
            var (parsedInput, extractedURLs) = IOconfCodeRepo.ExtractAndHideURLs(["CodeRepo; other; https://some.url.co"], []);
            ioConfMock.Setup(x => x.GetEntries<IOconfCodeRepo>()).Returns([new IOconfCodeRepo(parsedInput[0], 0)]);
            ioConfMock.Setup(x => x.GetCodeRepoURLs()).Returns(extractedURLs);
            var sut = new IOconfCode($"Code; default/somePlugin; 0.123.0; pc1", 1);

            // Act + Assert
            var ex = Assert.Throws<FormatException>(() => sut.ValidateDependencies(ioConfMock.Object));
            Assert.Contains("CodeRepo with name 'default' not found for Code line", ex.Message);
        }

        [TestMethod]
        public void ValidateDependencies_WhenMatchingCodeRepoIsConfigured_NoExceptionIsThrown()
        {
            // Arrange
            var ioConfMock = new Mock<IIOconf>();
            var (parsedInput, extractedURLs) = IOconfCodeRepo.ExtractAndHideURLs(["CodeRepo; other; https://some.url.co"], []);
            var codeRepo = new IOconfCodeRepo(parsedInput[0], 0);
            ioConfMock.Setup(x => x.GetEntries<IOconfCodeRepo>()).Returns([codeRepo]);
            ioConfMock.Setup(x => x.GetCodeRepoURLs()).Returns(extractedURLs);
            var sut = new IOconfCode($"Code; other/somePlugin; 0.123.0; pc1", 1);

            // Act
            sut.ValidateDependencies(ioConfMock.Object);

            // Assert
            Assert.AreEqual("somePlugin", sut.ClassName);
            Assert.AreEqual("other", sut.RepoName);
            Assert.AreSame(codeRepo, sut.CodeRepo);
        }
    }
}
