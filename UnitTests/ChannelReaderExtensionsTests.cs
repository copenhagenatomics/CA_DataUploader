#nullable enable
using CA_DataUploaderLib;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
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
            var reader = new DataVectorReader(channel.Reader);
            Assert.IsNull(await reader.ReadWithSoftTimeout(10, TimeProvider.System, CancellationToken.None));
        }

        [TestMethod]
        public async Task ReadWithSoftTimeoutAndCancellationTokenReturnsNull()
        {
            using var cts = new CancellationTokenSource();
            var channel = Channel.CreateUnbounded<DataVector>();
            var reader = new DataVectorReader(channel.Reader);
            Assert.IsNull(await reader.ReadWithSoftTimeout(10, TimeProvider.System, cts.Token));
        }

        [TestMethod]
        public async Task ReadBeforeTimeoutReturnsData()
        {
            var channel = Channel.CreateUnbounded<DataVector>();
            var reader = new DataVectorReader(channel.Reader);
            var task = reader.ReadWithSoftTimeout(10, TimeProvider.System, CancellationToken.None);
            channel.Writer.TryWrite(new(new[]{1d}, new(2021,1,1)));
            Assert.IsNotNull(await task);
        }
    }
}
