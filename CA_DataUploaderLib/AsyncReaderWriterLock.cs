using System;
using System.Threading;
using System.Threading.Tasks;

namespace CA_DataUploaderLib
{
    /// <summary>
    /// Adapted from original lightweight async reader/writer implementation on Stack Overflow:
    /// https://stackoverflow.com/a/64757462/7293142
    /// The answered question was then improved and posted via comment here:
    /// https://github.com/copenhagenatomics/CA_DataUploader/pull/90/files#diff-24a9664c904fe9276878f37dc1438aae578a76b7ef34eabbebf6ac66eaad83e6
    /// Then this https://gist.github.com/cajuncoding/a88f0d00847dcfc241ae80d1c7bafb1e version adds support for simplified using(){} notation via IDisposable so that Try/Finally blocks are not needed.
    ///
    /// Additional Notes:
    /// This is an async compatible reader / writer lock; this lock allows any amount of readers to enter the lock while only a single writer can do so at a time.
    /// While the writer holds the lock, any/all readers are blocked until the writer releases the lock. It doesn't have a lot of protection, so keep its usage simple (e.g. logic flows
    /// where using can be used and no recursion or re-entry is required.
    /// 
    /// Taking a reader lock: 
    /// <code>
    ///     using var _ = lock.AcquireReaderLock(cancellationToken);
    ///     // code goes here ... the lock is taken until the end of the current method
    /// </code>
    /// 
    /// Taking a writer lock: 
    /// <code>
    ///     using var _ = lock.AcquireWriterLock(cancellationToken);
    ///     // code goes here ... the lock is taken until the end of the current method
    /// </code>
    /// 
    /// How it works:
    ///  - Two semaphores amd a Count of readers in the lock are used to provide the above guarantees.
    ///  - To guarantee no new readers or writers can enter the lock while a writer is active, a write semaphore is used
    ///    - Both readers and writers acquire this semaphore first when trying to take the lock
    ///    - Readers then release the semaphore just after acquiring the read lock, so more readers can enter the lock (so technically acquiring of reader locks do not occur concurrently)
    ///  - To guarantee the writer does not enter the lock while there are still readers in the lock, a read semaphore is used
    ///    - Both the writer and the first reader acquire this semaphore when trying to take the lock, but they do this *after* they hold the write semaphore.
    ///    - The last active reader holding the lock, releases the read semaphore. Note it does not need to be the reader that acquired it first.
    ///    - To track if a reader acquiring/releasing a lock is the first/last one, a reader count is tracked when acquiring/releasing the read lock.
    ///  - Cancellation tokens are supported so that readers/writers can abort while waiting for an active writer to finish its job; which is easy to do with a timed expiration cancellation token.
    /// </summary>
    public sealed class AsyncReaderWriterLock : IDisposable
    {
        readonly SemaphoreSlim _readSemaphore = new(1, 1);
        readonly SemaphoreSlim _writeSemaphore = new(1, 1);
        int _readerCount;

        public async Task<IDisposable> AcquireWriterLock(CancellationToken token = default)
        {
            await _writeSemaphore.WaitAsync(token).ConfigureAwait(false);
            try
            {
                await _readSemaphore.WaitAsync(token).ConfigureAwait(false);
            }
            catch
            {
                _writeSemaphore.Release();
                throw;
            }

            return new LockToken(ReleaseWriterLock);

            void ReleaseWriterLock()
            {
                _readSemaphore.Release();
                _writeSemaphore.Release();
            }
        }

        public async Task<IDisposable> AcquireReaderLock(CancellationToken token = default)
        {
            await _writeSemaphore.WaitAsync(token).ConfigureAwait(false);
            if (Interlocked.Increment(ref _readerCount) == 1)
            {
                try
                {
                    await _readSemaphore.WaitAsync(token).ConfigureAwait(false);
                }
                catch
                {
                    Interlocked.Decrement(ref _readerCount);
                    _writeSemaphore.Release();
                    throw;
                }
            }

            _writeSemaphore.Release();
            return new LockToken(ReleaseReaderLock);

            void ReleaseReaderLock()
            {
                if (Interlocked.Decrement(ref _readerCount) == 0)
                    _readSemaphore.Release();
            }
        }

        public void Dispose()
        {
            _writeSemaphore.Dispose();
            _readSemaphore.Dispose();
        }

        private sealed class LockToken : IDisposable
        {
            private readonly Action _action;
            public LockToken(Action action) => _action = action;
            public void Dispose() => _action?.Invoke();
        }
    }
}