using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Pipelines;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CA_DataUploaderLib;
using Microsoft.Extensions.Time.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace UnitTests
{
    [TestClass]
    public class MCUBoardTests
    {
        [TestMethod]
        public async Task Processes4SingleValueLines()
        {//note the memorystream used in this test in reality simulates reading already read data before a disconnect,
         //which means this can't reproduce behaviors where logic waits for data since reads from the pipe either get any data 
         //and the pipe is detected as closed inmediately.
            var data = "2\n3\n4\n5\n";
            var dataBytes = Encoding.ASCII.GetBytes(data);
            using var ms = new MemoryStream(dataBytes);
            var reader = PipeReader.Create(ms);
            for (int i = 0; i < 4; i++)
                Assert.AreEqual((i + 2).ToString(), await ReadLine(reader));
        }

        [TestMethod]
        public async Task Processes2AvailableLinesWithoutWaitingForMoreData()
        {
            var pipe = new Pipe();
            var (reader, writer) = (pipe.Reader, pipe.Writer);
            await Write(writer, "1\n2\n");
            Assert.AreEqual("1", await ReadLine(reader));
            Assert.AreEqual("2", await ReadLine(reader));
        }

        [TestMethod]
        public async Task Processing2AvailableLinesDoesNotAffectThirdRead()
        {//this test reproduces a similar situation as Processes2AvailableLinesWithoutWaitingForMoreData,
         //but confirms the buffer permanently needs to get the extra line to do a read
         //which means we are getting stale data + have leaked memory for 1 line
            var pipe = new Pipe();
            var (reader, writer) = (pipe.Reader, pipe.Writer);
            await Write(writer, "1\n2\n");
            Assert.AreEqual("1", await ReadLine(reader));
            await Write(writer, "3\n");
            Assert.AreEqual("2", await ReadLine(reader));
            Assert.AreEqual("3", await ReadLine(reader));
        }

        [TestMethod]
        public async Task Processing2AvailableTwiceDoesNotAffectLaterReads()
        {//this test reproduces a similar situation as Processes2AvailableLinesWithoutWaitingForMoreData,
         //but confirms the amount of lines needed in the buffer keeps growing as the double lines situation repeats,
         //which means we keep leaking more memory over time + data becomes more stale 
            var pipe = new Pipe();
            var (reader, writer) = (pipe.Reader, pipe.Writer);
            await Write(writer, "1\n2\n");
            Assert.AreEqual("1", await ReadLine(reader));
            await Write(writer, "3\n");
            Assert.AreEqual("2", await ReadLine(reader));
            await Write(writer, "4\n5\n");
            Assert.AreEqual("3", await ReadLine(reader));
            Assert.AreEqual("4", await ReadLine(reader));
        }

        [TestMethod]
        public async Task ProcessingEmptyLineDoesNotStallInitialization()
        {//this test reproduces a similar situation as Processes2AvailableLinesWithoutWaitingForMoreData,
         //but confirms the buffer permanently needs to get the extra line to do a read
         //which means we are getting stale data + have leaked memory for 1 line
            var pipe = new Pipe();
            var (reader, writer) = (pipe.Reader, pipe.Writer);
            MCUBoard.AddCustomProtocol((buffer, portName) => default);
            var detectTask = MCUBoard.DetectThirdPartyProtocol(9600, "abc", reader);
            Assert.AreEqual(detectTask, await Task.WhenAny(detectTask, Task.Delay(4000)));
            await detectTask;
        }

        [TestMethod]
        public async Task ReadSerialRecognizesNormalHeader()
        {//this was *not* gathered from a box so might not be 100% accurate, but should be good enough
            var pipe = new Pipe();
            var (reader, writer) = (pipe.Reader, pipe.Writer);
            await Write(writer, "1\n");
            var expectSerialCommand = false;
            var header = new MCUBoard.Header();
            var res = header.DetectBoardHeader(reader, TryReadLine, () => Assert.IsTrue(expectSerialCommand,"unexpected Serial"), "myport");
            await Write(writer, "2\n");
            expectSerialCommand = true;
            await Write(writer, "3\n");
            expectSerialCommand = false;
            await Write(writer, "Serial Number: 1234\n");
            await Write(writer, "Product Type: Custom Board\n");
            await Write(writer, "Sub Product Type: 0\n");
            await Write(writer, "MCU Family: The new Mcu\n");
            await Write(writer, "Software Version: 1.2.3\n");
            await Write(writer, "Compile Date: 1-1-2023\n");
            await Write(writer, "Git SHA: abcd\n");
            await Write(writer, "PCB Version: 1.2\n");
            await Write(writer, "4:\n");
            Assert.IsTrue(await res);
            Assert.AreEqual("1234", header.SerialNumber);
            Assert.AreEqual("Custom Board", header.ProductType);
            Assert.AreEqual("0", header.SubProductType);
            Assert.AreEqual("The new Mcu", header.McuFamily);
            Assert.AreEqual("1.2.3", header.SoftwareVersion);
            Assert.AreEqual("1-1-2023", header.SoftwareCompileDate);
            Assert.AreEqual("abcd", header.GitSha);
            Assert.AreEqual("1.2", header.PcbVersion);
        }

        [TestMethod]
        public async Task ReadSerialRecognizesCalibration()
        {//this was *not* gathered from a box so might not be 100% accurate, but should be good enough
            var pipe = new Pipe();
            var (reader, writer) = (pipe.Reader, pipe.Writer);
            await Write(writer, "1\n");
            var expectSerialCommand = false;
            var header = new MCUBoard.Header();
            var res = header.DetectBoardHeader(reader, TryReadLine, () => Assert.IsTrue(expectSerialCommand,"unexpected Serial"), "myport");
            await Write(writer, "2\n");
            expectSerialCommand = true;
            await Write(writer, "3\n");
            expectSerialCommand = false;
            await Write(writer, "Serial Number: 1234\n");
            await Write(writer, "Product Type: Custom Board\n");
            await Write(writer, "Sub Product Type: 0\n");
            await Write(writer, "MCU Family: The new Mcu\n");
            await Write(writer, "Software Version: 1.2.3\n");
            await Write(writer, "Compile Date: 1-1-2023\n");
            await Write(writer, "Git SHA: abcd\n");
            await Write(writer, "PCB Version: 1.2\n");
            await Write(writer, "Calibration: abc\n");
            await Write(writer, "4:\n");
            Assert.IsTrue(await res);
            Assert.AreEqual("1234", header.SerialNumber);
            Assert.AreEqual("Custom Board", header.ProductType);
            Assert.AreEqual("0", header.SubProductType);
            Assert.AreEqual("The new Mcu", header.McuFamily);
            Assert.AreEqual("1.2.3", header.SoftwareVersion);
            Assert.AreEqual("1-1-2023", header.SoftwareCompileDate);
            Assert.AreEqual("abcd", header.GitSha);
            Assert.AreEqual("1.2", header.PcbVersion);
            Assert.AreEqual("abc", header.Calibration);
        }

        [TestMethod]
        public async Task ReadSerialRecognizesCalibrationAfterEmptyLine()
        {//this was *not* gathered from a box so might not be 100% accurate, but should be good enough
            var pipe = new Pipe();
            var (reader, writer) = (pipe.Reader, pipe.Writer);
            await Write(writer, "1\n");
            var expectSerialCommand = false;
            var header = new MCUBoard.Header();
            var res = header.DetectBoardHeader(reader, TryReadLine, () => Assert.IsTrue(expectSerialCommand,"unexpected Serial"), "myport");
            await Write(writer, "2\n");
            expectSerialCommand = true;
            await Write(writer, "3\n");
            expectSerialCommand = false;
            await Write(writer, "Serial Number: 1234\n");
            await Write(writer, "Product Type: Custom Board\n");
            await Write(writer, "Sub Product Type: 0\n");
            await Write(writer, "MCU Family: The new Mcu\n");
            await Write(writer, "Software Version: 1.2.3\n");
            await Write(writer, "Compile Date: 1-1-2023\n");
            await Write(writer, "Git SHA: abcd\n");
            await Write(writer, "PCB Version: 1.2\n");
            await Write(writer, "\n");
            await Write(writer, "Calibration: abc\n");
            await Write(writer, "4:\n");
            Assert.IsTrue(await res);
            Assert.AreEqual("1234", header.SerialNumber);
            Assert.AreEqual("Custom Board", header.ProductType);
            Assert.AreEqual("0", header.SubProductType);
            Assert.AreEqual("The new Mcu", header.McuFamily);
            Assert.AreEqual("1.2.3", header.SoftwareVersion);
            Assert.AreEqual("1-1-2023", header.SoftwareCompileDate);
            Assert.AreEqual("abcd", header.GitSha);
            Assert.AreEqual("1.2", header.PcbVersion);
            Assert.AreEqual("abc", header.Calibration);
        }

        [TestMethod]
        public async Task ReadSerialRecognizesWithMissingValues()
        {
            var pipe = new Pipe();
            var (reader, writer) = (pipe.Reader, pipe.Writer);
            await Write(writer, "1\n");
            var expectSerialCommand = false;
            var header = new MCUBoard.Header();
            var res = header.DetectBoardHeader(reader, TryReadLine, () => Assert.IsTrue(expectSerialCommand,"unexpected Serial"), "myport");
            await Write(writer, "2\n");
            expectSerialCommand = true;
            await Write(writer, "3\n");
            expectSerialCommand = false;
            await Write(writer, "Serial Number:\n");
            await Write(writer, "Product Type:\n");
            await Write(writer, "Sub Product Type:\n");
            await Write(writer, "MCU Family:\n");
            await Write(writer, "Software Version:\n");
            await Write(writer, "Compile Date:\n");
            await Write(writer, "Git SHA:\n");
            await Write(writer, "PCB Version:\n");
            await Write(writer, "4:\n");
            Assert.IsTrue(await res);
            Assert.AreEqual("", header.SerialNumber);
            Assert.AreEqual("", header.ProductType);
            Assert.AreEqual("", header.SubProductType);
            Assert.AreEqual("", header.McuFamily);
            Assert.AreEqual("", header.SoftwareVersion);
            Assert.AreEqual("", header.SoftwareCompileDate);
            Assert.AreEqual("", header.GitSha);
            Assert.AreEqual("", header.PcbVersion);
        }

        [TestMethod]
        public async Task ReadSerialLogsReadLinesOnMissingHeader()
        {
            var pipe = new Pipe();
            var (reader, writer) = (pipe.Reader, pipe.Writer);
            await Write(writer, "Reconnected Reason: watchdog\n");
            await Write(writer, "1\n");
            var time = new FakeTimeProvider();
            var logger = new StringBuilderLogger();
            var expectSerialCommand = false;
            var header = new MCUBoard.Header(new(time, logger, Mock.Of<MCUBoard.IConnectionManager>(MockBehavior.Strict)));
            var res = header.DetectBoardHeader(reader, TryReadLine, () => Assert.IsTrue(expectSerialCommand,"unexpected Serial"), "myport");
            await Write(writer, "2\n");
            expectSerialCommand = true;
            await Write(writer, "3\n");
            expectSerialCommand = false;
            await Write(writer, "Serial Number: 123\n");
            await Write(writer, "Some unexpected failure\n");
            await Write(writer, "4:\n");
            Assert.IsFalse(res.IsCompleted, "unexpected early completion");
            await Task.Yield();
            time.Advance(TimeSpan.FromSeconds(5000));
            Assert.IsTrue(await res, "unexpected not able to read response when the board is returning data");
            Assert.AreEqual("123", header.SerialNumber);
            var expectedLog =
@"error-A-Partial board header detected from myport: timed out
Reconnected Reason: watchdog
1
2
3
Serial Number: 123
Some unexpected failure
4:

";
            Assert.AreEqual(expectedLog, logger.ToString());
            
        }

        [TestMethod]
        public async Task ReadSerialSkipsEmptyLinesAfterTheHeader()
        {//this was *not* gathered from a box so might not be 100% accurate, but should be good enough
            var pipe = new Pipe();
            var (reader, writer) = (pipe.Reader, pipe.Writer);
            await Write(writer, "1\n");
            var expectSerialCommand = false;
            var header = new MCUBoard.Header();
            var res = header.DetectBoardHeader(reader, TryReadLine, () => Assert.IsTrue(expectSerialCommand,"unexpected Serial"), "myport");
            await Write(writer, "2\n");
            expectSerialCommand = true;
            await Write(writer, "3\n");
            expectSerialCommand = false;
            await Write(writer, "Serial Number: 1234\n");
            await Write(writer, "Product Type: Custom Board\n");
            await Write(writer, "Sub Product Type: 0\n");
            await Write(writer, "MCU Family: The new Mcu\n");
            await Write(writer, "Software Version: 1.2.3\n");
            await Write(writer, "Compile Date: 1-1-2023\n");
            await Write(writer, "Git SHA: abcd\n");
            await Write(writer, "PCB Version: 1.2\n");
            await Write(writer, "\n");
            await Write(writer, "Calibration: abc\n");
            await Write(writer, "\n");
            await Write(writer, "\n");
            await Write(writer, "\n");
            await Write(writer, "4\n");
            Assert.IsTrue(await res);
            Assert.AreEqual("4", await ReadLine(reader));
        }



        private static Task<string> ReadLine(PipeReader reader) => MCUBoard.ReadLine(MCUBoard.Dependencies.Default, reader, "abc", 16, TryReadLine, CancellationToken.None);
        private static ValueTask<FlushResult> Write(PipeWriter writer, string data) => Write(writer, Encoding.ASCII.GetBytes(data));
        private static ValueTask<FlushResult> Write(PipeWriter writer, byte[] bytes)
        {
            var span = writer.GetSpan(bytes.Length);
            bytes.AsSpan().CopyTo(span);
            writer.Advance(bytes.Length);
            return writer.FlushAsync();
        }

        private static bool TryReadLine(ref ReadOnlySequence<byte> buffer, [NotNullWhen(true)] out string? line) => MCUBoard.TryReadAsciiLine(ref buffer, out line, '\n');

        private class StringBuilderLogger : ILog
        {
            private readonly StringBuilder sb = new();
            public void LogData(LogID id, string msg) => sb.AppendLine($"data-{id}-{msg}");
            public void LogError(LogID id, string msg, Exception ex) => sb.AppendLine($"error-{id}-{msg}-{ex}");
            public void LogError(LogID id, string msg) => sb.AppendLine($"error-{id}-{msg}");
            public void LogInfo(LogID id, string msg) => sb.AppendLine($"info-{id}-{msg}");
            public override string ToString() => sb.ToString();
        }
    }
}