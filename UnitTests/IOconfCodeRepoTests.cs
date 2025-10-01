using CA_DataUploaderLib.IOconf;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace UnitTests
{
    [TestClass]
    public class IOconfCodeRepoTests
    {
        [TestMethod]
        public void ExtractAndHideURLs_WhenUrlIsMalformed_ThrowsException()
        {
            // Act + Assert
            var ex = Assert.ThrowsException<FormatException>(() => IOconfCodeRepo.ExtractAndHideURLs(["CodeRepo; test; horse/::some..url.co"], []));
            Assert.IsTrue(ex.Message.StartsWith("Invalid URL"));
        }

        [TestMethod]
        public void ExtractAndHideURLs_WhenUrlIsMissing_ThrowsException()
        {
            // Act + Assert
            var ex = Assert.ThrowsException<FormatException>(() => IOconfCodeRepo.ExtractAndHideURLs(["CodeRepo; test"], []));
            Assert.IsTrue(ex.Message.StartsWith("Missing URL"));
        }

        [TestMethod]
        public void ExtractAndHideURLs_WhenUrlIsAlreadyHidden_NothingIsExtracted()
        {
            // Act
            var (_, extractedURLs) = IOconfCodeRepo.ExtractAndHideURLs([$"CodeRepo; test; {IOconfCodeRepo.HiddenURL}"], []);

            // Assert
            Assert.AreEqual(0, extractedURLs.Count);
        }

        [TestMethod]
        public void ExtractAndHideURLs_ReplacesUrl()
        {
            // Arrange
            List<string> input = [$"CodeRepo; repoA; https://example.com/repoA/", "CodeRepo;repoB;https://example.com/repoB/?si=123"];
            List<string> expectedOutput = [$"CodeRepo; repoA; {IOconfCodeRepo.HiddenURL}", $"CodeRepo;repoB;{IOconfCodeRepo.HiddenURL}"];

            // Act
            var (result, extractedURLs) = IOconfCodeRepo.ExtractAndHideURLs(input, []);

            // Assert
            CollectionAssert.AreEqual(expectedOutput, result);
            CollectionAssert.AreEqual(new Dictionary<string, string> { { "repoA", "https://example.com/repoA/" }, { "repoB", "https://example.com/repoB/?si=123" } }, extractedURLs);
        }

        [TestMethod]
        public void ExtractAndHideURLs_EnsurePathHasTrailingForwardSlash()
        {
            // Arrange
            List<string> input = [$"CodeRepo; repoA; https://example.com/without", "CodeRepo;repoB;https://example.com/repoB?si=123"];
            List<string> expectedOutput = [$"CodeRepo; repoA; {IOconfCodeRepo.HiddenURL}", $"CodeRepo;repoB;{IOconfCodeRepo.HiddenURL}"];

            // Act
            var (result, extractedURLs) = IOconfCodeRepo.ExtractAndHideURLs(input, []);

            // Assert
            CollectionAssert.AreEqual(expectedOutput, result);
            CollectionAssert.AreEqual(new Dictionary<string, string> { { "repoA", "https://example.com/without/" }, { "repoB", "https://example.com/repoB/?si=123" } }, extractedURLs);
        }

        [TestMethod]
        public void ExtractAndHideURLs_OnlyProcessesCodeRepoLines()
        {
            // Arrange
            List<string> input = [$"CodeRepo; repoA; https://example.com/repoA/", "OtherType; repoB; https://example.com/repoB/"];
            List<string> expectedOutput = [$"CodeRepo; repoA; {IOconfCodeRepo.HiddenURL}", "OtherType; repoB; https://example.com/repoB/"];

            // Act
            var (result, extractedURLs) = IOconfCodeRepo.ExtractAndHideURLs(input, []);

            // Assert
            CollectionAssert.AreEqual(expectedOutput, result);
            CollectionAssert.AreEqual(new Dictionary<string, string> { { "repoA", "https://example.com/repoA/" } }, extractedURLs);
        }

        [TestMethod]
        public void ExtractAndHideURLs_AppendsToExistingJson()
        {
            // Arrange
            var initialDict = new Dictionary<string, string> { { "repoA", "https://oldurl.com/repoA/" } };
            List<string> input = ["CodeRepo; repoB; https://example.com/repoB/"];
            List<string> expectedOutput = [$"CodeRepo; repoB; {IOconfCodeRepo.HiddenURL}"];

            // Act
            var (result, extractedURLs) = IOconfCodeRepo.ExtractAndHideURLs(input, initialDict);

            // Assert
            CollectionAssert.AreEqual(expectedOutput, result);
            CollectionAssert.AreEqual(new Dictionary<string, string> { { "repoA", "https://oldurl.com/repoA/" }, { "repoB", "https://example.com/repoB/" } }, extractedURLs);
        }

        [TestMethod]
        public void ExtractAndHideURLs_UpdateExistingURL()
        {
            // Arrange
            var initialDict = new Dictionary<string, string> { { "repoA", "https://oldurl.com/repoA/" } };
            List<string> input = ["CodeRepo; repoA; https://newurl.com/repoA/"];
            List<string> expectedOutput = [$"CodeRepo; repoA; {IOconfCodeRepo.HiddenURL}"];

            // Act
            var (result, extractedURLs) = IOconfCodeRepo.ExtractAndHideURLs(input, initialDict);

            // Assert
            CollectionAssert.AreEqual(expectedOutput, result);
            CollectionAssert.AreEqual(extractedURLs, new Dictionary<string, string> { { "repoA", "https://newurl.com/repoA/" } });
        }

        [TestMethod]
        public void Constructor_ClearTextUrl_ThrowsException()
        {
            // Act + Assert
            var ex = Assert.ThrowsException<FormatException>(() => new IOconfCodeRepo("CodeRepo; repoA; https://example.com/repoA/", 0));
            Assert.IsTrue(ex.Message.StartsWith("Raw URL"), ex.Message);
        }

        [TestMethod]
        public void LoadURL_WhenUrlIsNotFound_ThrowsException()
        {
            // Arrange
            List<string> input = [$"CodeRepo; repoA; https://example.com/repoA/"];
            var (parsedInput, extractedURLs) = IOconfCodeRepo.ExtractAndHideURLs(input, []);
            var sut = new IOconfCodeRepo($"CodeRepo; repoB; {IOconfCodeRepo.HiddenURL}", 0);
            var ioConfMock = new Mock<IIOconf>();
            ioConfMock.Setup(x => x.GetCodeRepoURLs()).Returns(extractedURLs);

            // Act + Assert
            var ex = Assert.ThrowsException<FormatException>(() => sut.LoadURL(ioConfMock.Object));
            Assert.IsTrue(ex.Message.Contains("not found"), ex.Message);
        }

        [TestMethod]
        public void LoadURL_WhenUrlIsFound()
        {
            // Arrange
            var url = "https://example.com/repoA/";
            List<string> input = [$"CodeRepo; repoA; {url}"];
            var (parsedInput, extractedURLs) = IOconfCodeRepo.ExtractAndHideURLs(input, []);
            var ioConfMock = new Mock<IIOconf>();
            ioConfMock.Setup(x => x.GetCodeRepoURLs()).Returns(extractedURLs);
            var sut = new IOconfCodeRepo(parsedInput[0], 0);

            // Act + Assert
            sut.LoadURL(ioConfMock.Object);
            Assert.AreEqual(url, sut.URL);
        }

        [TestMethod]
        public void ReadWriteURLsToFile_NewFile()
        {
            // Arrange
            if (File.Exists(IOconfCodeRepo.RepoUrlJsonFile))
                File.Delete(IOconfCodeRepo.RepoUrlJsonFile);
            var urls = new Dictionary<string, string> { { "repoA", "https://example.com/repoA/?sv=123&si=ca" }, { "repoB", "https://newurl.com/repoB/" } };

            // Act
            IOconfCodeRepo.WriteURLsToFile(urls);

            // Assert
            Assert.IsTrue(File.Exists(IOconfCodeRepo.RepoUrlJsonFile));
            CollectionAssert.AreEqual(urls, IOconfCodeRepo.ReadURLsFromFile());
        }

        [TestMethod]
        public void ReadWriteURLsToFile_AppendToExistingFile()
        {
            // Arrange
            if (File.Exists(IOconfCodeRepo.RepoUrlJsonFile))
                File.Delete(IOconfCodeRepo.RepoUrlJsonFile);
            var existingURLs = new Dictionary<string, string> { { "repoA", "https://example.com/repoA/" }, { "repoB", "https://newurl.com/repoB/" } };
            IOconfCodeRepo.WriteURLsToFile(existingURLs);
            var newURL = new Dictionary<string, string> { { "repoC", "https://example.com/repoC/" } };

            // Act
            IOconfCodeRepo.WriteURLsToFile(newURL);

            // Assert
            Assert.IsTrue(File.Exists(IOconfCodeRepo.RepoUrlJsonFile));
            CollectionAssert.AreEqual(existingURLs.Union(newURL).ToDictionary(), IOconfCodeRepo.ReadURLsFromFile());
        }

        [TestMethod]
        public void ReadWriteURLsToFile_UpdateExistingFile()
        {
            // Arrange
            if (File.Exists(IOconfCodeRepo.RepoUrlJsonFile))
                File.Delete(IOconfCodeRepo.RepoUrlJsonFile);
            var existingURLs = new Dictionary<string, string> { { "repoA", "https://example.com/repoA/" }, { "repoB", "https://example.com/repoB/" } };
            IOconfCodeRepo.WriteURLsToFile(existingURLs);
            var newURL = new Dictionary<string, string> { { "repoA", "https://newurl.com/repoA/" } };

            // Act
            IOconfCodeRepo.WriteURLsToFile(newURL);

            // Assert
            Assert.IsTrue(File.Exists(IOconfCodeRepo.RepoUrlJsonFile));
            CollectionAssert.AreEqual(new Dictionary<string, string> { { "repoA", "https://newurl.com/repoA/" }, { "repoB", "https://example.com/repoB/" } }, IOconfCodeRepo.ReadURLsFromFile());
        }

        [TestMethod]
        public void ReadURLsFromFile_SurvivesCorruptFile()
        {
            // Arrange
            if (File.Exists(IOconfCodeRepo.RepoUrlJsonFile))
                File.Delete(IOconfCodeRepo.RepoUrlJsonFile);
            File.WriteAllText(IOconfCodeRepo.RepoUrlJsonFile, "blablabla");

            // Act
            var urls = IOconfCodeRepo.ReadURLsFromFile();

            // Assert
            Assert.AreEqual(0, urls.Count);
        }

        [TestMethod]
        public void GenerateDownloadUrl_WithoutQueryParameters_TrailingForwardSlash()
        {
            // Arrange
            List<string> input = [$"CodeRepo; repoA; https://example.com/repoA/"];
            var (parsedInput, extractedURLs) = IOconfCodeRepo.ExtractAndHideURLs(input, []);
            var ioConfMock = new Mock<IIOconf>();
            ioConfMock.Setup(x => x.GetCodeRepoURLs()).Returns(extractedURLs);
            var sut = new IOconfCodeRepo(parsedInput[0], 0);
            sut.LoadURL(ioConfMock.Object);

            // Act + Assert
            var downloadUrl = sut.GenerateDownloadUrl("file");
            Assert.AreEqual(new Uri("https://example.com/repoA/file"), downloadUrl);
        }

        [TestMethod]
        public void GenerateDownloadUrl_WithoutQueryParameters_NoTrailingForwardSlash()
        {
            // Arrange
            List<string> input = [$"CodeRepo; repoA; https://example.com/repoA"]; // <- no trailing '/'
            var (parsedInput, extractedURLs) = IOconfCodeRepo.ExtractAndHideURLs(input, []);
            var ioConfMock = new Mock<IIOconf>();
            ioConfMock.Setup(x => x.GetCodeRepoURLs()).Returns(extractedURLs);
            var sut = new IOconfCodeRepo(parsedInput[0], 0);
            sut.LoadURL(ioConfMock.Object);

            // Act + Assert
            var downloadUrl = sut.GenerateDownloadUrl("file.dll");
            Assert.AreEqual(new Uri("https://example.com/repoA/file.dll"), downloadUrl);
        }

        [TestMethod]
        public void GenerateDownloadUrl_WithQueryParameters()
        {
            // Arrange
            var url = "https://example.com/repoA/?secrets=1234567890&goes=here";
            List<string> input = [$"CodeRepo; repoA; {url}"];
            var (parsedInput, extractedURLs) = IOconfCodeRepo.ExtractAndHideURLs(input, []);
            var ioConfMock = new Mock<IIOconf>();
            ioConfMock.Setup(x => x.GetCodeRepoURLs()).Returns(extractedURLs);
            var sut = new IOconfCodeRepo(parsedInput[0], 0);
            sut.LoadURL(ioConfMock.Object);

            // Act + Assert
            var downloadUrl = sut.GenerateDownloadUrl("file.dll");
            Assert.AreEqual(new Uri("https://example.com/repoA/file.dll?secrets=1234567890&goes=here"), downloadUrl);
        }
    }
}
