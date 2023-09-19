using System.Threading.Channels;
using System.Threading;
using System.Threading.Tasks;

namespace CA_DataUploaderLib.Extensions
{
    public static class ChannelReaderExtensions
    {
        /// <returns><c>null</c> if it timed out, otherwise the read value.</returns>
        public static async Task<T?> ReadWithSoftTimeout<T>(this ChannelReader<T> reader, int timeoutMs, CancellationToken token)
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            linkedCts.CancelAfter(timeoutMs);
            var nextVectorTask = await Task.WhenAny(reader.ReadAsync(linkedCts.Token).AsTask()); //Task.Any avoids an exception so we can return null on timeouts instead
            return nextVectorTask.IsCompletedSuccessfully ? await nextVectorTask : default;
        }
    }
}
