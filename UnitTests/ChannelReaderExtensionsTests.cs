#nullable enable
using CA_DataUploaderLib;
using CA_DataUploaderLib.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace UnitTests
{
    [TestClass]
    public class ChannelReaderExtensionsTests
    {
        [TestMethod]
        public async Task ReadWithSoftTimeoutReturnsNull()
        {
            var channel = Channel.CreateUnbounded<DataVector>();
            Assert.IsNull(await channel.Reader.ReadWithSoftTimeout(10, CancellationToken.None));
        }

        [TestMethod]
        public async Task ReadWithSoftTimeoutAndCancellationTokenReturnsNull()
        {
            using var cts = new CancellationTokenSource();
            var channel = Channel.CreateUnbounded<DataVector>();
            Assert.IsNull(await channel.Reader.ReadWithSoftTimeout(10, cts.Token));
        }

        [TestMethod]
        public async Task ReadBeforeTimeoutReturnsData()
        {
            var channel = Channel.CreateUnbounded<DataVector>();
            var task = channel.Reader.ReadWithSoftTimeout(10, CancellationToken.None);
            channel.Writer.TryWrite(new(new[]{1d}, new(2021,1,1)));
            Assert.IsNotNull(await task);
        }
    }
}
