using Microsoft.VisualStudio.TestTools.UnitTesting;
using CA_DataUploaderLib;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

namespace UnitTests
{
    [TestClass]
    public class SingleThreadRunnerTests
    {
        [TestMethod]
        public async Task SingleThreadRunner()
        {
            var runner = new MCUBoard.SingleThreadRunner();
            // note, we use a method for IncreaseCounter instead of a lambda to make sure there are no weird side effects of capturing the thread static counter
            var tasks = Enumerable.Range(0, 3).Select(_ => runner.Run(GetThreadId, CancellationToken.None)).ToList(); 
            var lastId = await runner.Run(GetThreadId, CancellationToken.None);
            CollectionAssert.AreEqual(new []{true, true, true}, tasks.Select(t => t.IsCompleted).ToList(), "all previous tasks must also be done after the last one is awaited");
            var ids = await Task.WhenAll(tasks); // make sure the tasks have finished running
            Assert.AreEqual(lastId, ids[0], "last and first did not ran in same thread");
            Assert.AreEqual(lastId, ids[1], "last and second did not ran in same thread");
            Assert.AreEqual(lastId, ids[2], "last and third did not ran in same thread");
            var extraRunId = await runner.Run(GetThreadId, CancellationToken.None);
            Assert.AreEqual(lastId, ids[0], "last and extra did not ran in same thread");
            var timedRun = runner.Run(GetThreadId, CancellationToken.None);
            var executedTask = await Task.WhenAny(timedRun, Task.Delay(1)); // in reality delay is allowing more than 1 ms (the minimum clock frequency)
            Assert.AreEqual(timedRun, executedTask, "task did not finish within a minimum delay");
            Assert.IsTrue(timedRun.IsCompleted, "task did not finish within a minimum delay");
            var finished = false;
            runner.finished += (sender, args) => finished = true;
            runner.Dispose();
            await Task.Delay(1); // in reality delay is allowing more than 1 ms (the minimum clock frequency)
            Assert.IsTrue(finished, "runner loop did not stop a minimum delay after dispose");
            Assert.AreNotEqual(Thread.CurrentThread.ManagedThreadId, lastId, "runner should run on its own thread");
        }

        private static int GetThreadId() => Thread.CurrentThread.ManagedThreadId;
    }
}
