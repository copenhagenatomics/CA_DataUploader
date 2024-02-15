using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace UnitTests
{
    internal class ParallelHelper
    {
        public static Task ForRacingThreads(int start, int count, Func<int, Func<Task>> getAction) => 
            RunRacingThreads(Enumerable.Range(start, count).Select(i => getAction(i)).ToList());
        public static Task ForRacingThreads(int start, int count, Func<int, Action> getAction) =>
            RunRacingThreads(Enumerable.Range(start, count).Select<int, Func<Task>>(i =>
            {
                var action = getAction(i);
                return () => { action(); return Task.CompletedTask; };
            }).ToList());
        public static async Task RunRacingThreads(IReadOnlyCollection<Func<Task>> actions)
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
