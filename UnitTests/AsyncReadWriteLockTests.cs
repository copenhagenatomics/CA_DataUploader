using CA_DataUploaderLib;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace UnitTests
{
    //unit test inspired by https://gist.github.com/cajuncoding/a88f0d00847dcfc241ae80d1c7bafb1e
    [TestClass]
    public class AsyncReadWriteLockTests
    {
        [TestMethod]
        public async Task TestAsyncReadWriteLockForBlockedWrites()
        {
            //Our race condition test works by having a mix of readers and writers that report they ran to results.
            //Readers report -1 while writers report values 0 - 10. If the reader/writer lock is working, all 10 values from the writer appear together in order in the results.
            var asyncReadWriteLock = new AsyncReaderWriterLock();
            var results = new ConcurrentQueue<(int index, int value)>();
            int writeBlockSize = 40, readersForEveryWriter = 9;
            bool IsWriter(int index) => index % (readersForEveryWriter + 1) == 0;
            await ForRacingThreads(0, 100, i => async () =>
            {
                if (IsWriter(i))
                {//writer: report values for the write block
                    using var _ = await asyncReadWriteLock.AcquireWriterLock().ConfigureAwait(false);
                    for (var r = 0; r < writeBlockSize; r++)
                    {
                        results.Enqueue((i, r));
                        await Task.Yield();
                    }
                }
                else
                {//reader: report -1
                    using var _ = await asyncReadWriteLock.AcquireReaderLock().ConfigureAwait(false);
                    results.Enqueue((i, -1));
                }
            }).ConfigureAwait(false);

            var resultsList = results.ToList();
            var failureMessage = $"unexpected value detected. Results: {string.Join(Environment.NewLine, resultsList)}";
            for (var i = 0; i < resultsList.Count; i++)
            {
                var index = resultsList[i].index;
                if (IsWriter(index))
                {//check write block was reported together (without values of other readers or writers in between)
                    for (var r = 0; r < writeBlockSize; r++)
                        Assert.AreEqual((index, r), resultsList[i + r], failureMessage);
                    i += writeBlockSize - 1; //-1 because the outer for will increment it again
                }
                else//check reader reported -1
                    Assert.AreEqual((index, -1), resultsList[i], failureMessage);
            }
        }

        private static Task ForRacingThreads(int start, int count, Func<int, Func<Task>> getAction) => 
            RunRacingThreads(Enumerable.Range(start, count).Select(i => getAction(i)).ToList());
        private static async Task RunRacingThreads(IReadOnlyCollection<Func<Task>> actions)
        {
            //We use a CountdownEvent to ensure all threads are created and ready to race before running the actions.
            //We use SetMinThreads to ensure we have enough pool threads, as otherwise it takes way too long to start or even blocks
            ThreadPool.SetMinThreads(actions.Count, actions.Count);
            var readyEvent = new CountdownEvent(actions.Count);
            await Task.WhenAll(actions
                .Select(action => Task.Run(() => 
                { 
                    readyEvent.Signal(); //we are ready to run
                    readyEvent.Wait();  //wait for all others to be ready to run
                    return action(); 
                }))
                ).ConfigureAwait(false);
        }
    }
}
