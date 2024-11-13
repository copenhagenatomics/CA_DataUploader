using System;
using System.Threading.Channels;
using System.Threading;
using System.Threading.Tasks;

namespace CA_DataUploaderLib.Extensions
{
    public static class ChannelReaderExtensions
    {
        /// <returns><c>null</c> if it timed out, otherwise the read value.</returns>
        public static async Task<T?> ReadWithSoftTimeout<T>(this ChannelReader<T> reader, int timeoutMs, TimeProvider time, CancellationToken token)
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMs), time);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token);
            var nextVectorTask = await Task.WhenAny(reader.ReadAsync(linkedCts.Token).AsTask()); //Task.Any avoids an exception so we can return null on timeouts instead
            return nextVectorTask.IsCompletedSuccessfully ? await nextVectorTask : default;
        }
    }
}
