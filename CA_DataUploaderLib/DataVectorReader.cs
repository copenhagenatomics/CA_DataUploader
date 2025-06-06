#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Channels;
using System.Runtime.CompilerServices;

namespace CA_DataUploaderLib
{
    public class DataVectorReader(ChannelReader<DataVector> reader)
    {
        private DateTime previousVectorReadByReadWithSoftTimeout;
        public DateTime LastVectorTimeProcessed { get; set; }
        public async IAsyncEnumerable<DataVector> ReadAllAsync([EnumeratorCancellation] CancellationToken token)
        {
            await foreach (var vector in reader.ReadAllAsync(token))
            {
                yield return vector;
                //we consider the vector processed by the caller, as it typically would ask for the next vector when its done with the previous one.
                LastVectorTimeProcessed = vector.Timestamp; 
            }
        }

        public async Task<DataVector?> ReadWithSoftTimeout(int timeoutMs, TimeProvider time, CancellationToken token)
        {
            //we consider the previous vector proceesed by the caller, as it typically would ask for the next vector when its done with the previous one.
            LastVectorTimeProcessed = previousVectorReadByReadWithSoftTimeout;
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMs), time);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token);
            var nextVectorTask = await Task.WhenAny(reader.ReadAsync(linkedCts.Token).AsTask()); //Task.Any avoids an exception so we can return null on timeouts instead
            if (!nextVectorTask.IsCompletedSuccessfully)
                return default;

            var vector = await nextVectorTask;
            previousVectorReadByReadWithSoftTimeout = vector.Timestamp; //note we only get here if we managed to read the vector within the timeout
            return vector;
        }
    }
}
