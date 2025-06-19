#nullable enable
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static CA_DataUploaderLib.BaseSensorBox;

namespace UnitTests
{
    [TestClass]
    public class HighResolutionWriterTests
    {
        private static string CreateTempDirectory()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "HighResWriterTest_" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            return tempDir;
        }

        [TestMethod]
        public async Task WriteLineAsync_WritesAndFlushesToZip()
        {
            // Arrange
            string tempDir = CreateTempDirectory();
            string name = "testBoard";
            var writer = new HighResolutionWriter(tempDir, name, s => { });

            // Act
            await writer.WriteLineAsync("header1,header2", default);
            await writer.WriteLineAsync("1,2", default);
            await writer.StopAsync(default);

            // Assert
            var zipFiles = Directory.GetFiles(tempDir, $"HighResolution_{name}_*.zip");
            Assert.AreEqual(1, zipFiles.Length, "Expected one zip file to be created.");
            using var archive = ZipFile.OpenRead(zipFiles[0]);
            var entry = archive.Entries.FirstOrDefault(e => e.FullName.EndsWith(".csv"));
            Assert.IsNotNull(entry, "CSV entry not found in zip.");
            using var entryStream = entry.Open();
            using var reader = new StreamReader(entryStream);
            string content = reader.ReadToEnd();
            Assert.IsTrue(content.Contains("header1,header2"));
            Assert.IsTrue(content.Contains("1,2"));
        }

        [TestMethod]
        public async Task WriteLineAsync_TriggersFlushWhenStreamIsLarge()
        {
            // Arrange
            string tempDir = CreateTempDirectory();
            string name = "testBoard2";
            var writer = new HighResolutionWriter(tempDir, name, s => {});

            // Act - Write enough lines to exceed 900,000 bytes
            var longLine = new string('A', 10000);
            for (int i = 0; i < 100; i++)
                await writer.WriteLineAsync(longLine, default);
            
            // Assert - Should have flushed at least once
            var zipFiles = Directory.GetFiles(tempDir, $"HighResolution_{name}_*.zip");
            Assert.IsTrue(zipFiles.Length > 0, "Expected at least one zip file to be created after large writes.");
        }

        [TestMethod]
        public async Task StopAsync_ResetsStream()
        {
            // Arrange
            string tempDir = CreateTempDirectory();
            string name = "testBoard3";
            var timeProvider = new FakeTimeProvider(DateTime.Now);
            var writer = new HighResolutionWriter(tempDir, name, s => { }, timeProvider);

            // Act - Write and stop twice
            await writer.WriteLineAsync("header1,header2", default);
            await writer.StopAsync(default);
            timeProvider.Advance(TimeSpan.FromSeconds(1)); // Simulate time passing
            await writer.WriteLineAsync("header1,header2", default);
            await writer.StopAsync(default);

            // Assert - Should create two files
            var zipFiles = Directory.GetFiles(tempDir, $"HighResolution_{name}_*.zip");
            Assert.IsTrue(zipFiles.Length == 2, $"Expected two zip files after two stops, but seeing {zipFiles.Length}.");
        }

        [TestMethod]
        public async Task StopAsync_RespectsMaxFilesInFolder()
        {
            // Arrange
            string tempDir = CreateTempDirectory();
            string name = "testBoard4";
            var timeProvider = new FakeTimeProvider(DateTime.Now);
            var writer = new HighResolutionWriter(tempDir, name, s => { }, timeProvider);

            // Act
            for (int i = 0; i < HighResolutionWriter.MaxFilesInFolder + 5; i++)
            {
                await writer.WriteLineAsync($"header{i}", default);
                await writer.StopAsync(default);
                timeProvider.Advance(TimeSpan.FromSeconds(1)); // Simulate time passing
            }

            // Assert
            var zipFiles = Directory.GetFiles(tempDir, $"HighResolution_{name}_*.zip");
            Assert.IsTrue(zipFiles.Length == HighResolutionWriter.MaxFilesInFolder, $"Should not exceed MaxFilesInFolder limit {zipFiles.Length} != {HighResolutionWriter.MaxFilesInFolder}.");
        }
    }
}
