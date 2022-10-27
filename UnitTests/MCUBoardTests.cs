using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Pipelines;
using System.Text;
using System.Threading.Tasks;
using CA_DataUploaderLib;
using Microsoft.VisualStudio.TestTools.UnitTesting;

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

        private static Task<string> ReadLine(PipeReader reader) => MCUBoard.ReadLine(reader, "abc", 16, TryReadLine);
        private static ValueTask<FlushResult> Write(PipeWriter writer, string data) => Write(writer, Encoding.ASCII.GetBytes(data));
        private static ValueTask<FlushResult> Write(PipeWriter writer, byte[] bytes)
        {
            var span = writer.GetSpan(bytes.Length);
            bytes.AsSpan().CopyTo(span);
            writer.Advance(bytes.Length);
            return writer.FlushAsync();
        }

        private static bool TryReadLine(ref ReadOnlySequence<byte> buffer, [NotNullWhen(true)] out string? line) =>
            MCUBoard.TryReadAsciiLine(ref buffer, out line, '\n');
    }
}