using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace CA_DataUploaderLib
{
    ///<summary>Throttle calls to mantain a given frequency</summary>
    public class TimeThrottle
    {
        private readonly Stopwatch _watch = Stopwatch.StartNew();
        private long _nextTriggerElapsedMilliseconds;
        private readonly int _milliseconds;

        ///<param name="milliseconds">this is how many milliseconds it will attempt to keep between each call</param>
        public TimeThrottle(int milliseconds)
        {
            _milliseconds = milliseconds;
        }

        public Task WaitAsync() => Task.Delay(GetWaitMilliseconds());
        public void Wait() => Thread.Sleep(GetWaitMilliseconds());
        public void Restart() => _watch.Restart();

        private int GetWaitMilliseconds()
        {
            _nextTriggerElapsedMilliseconds += _milliseconds;
            return (int)Math.Max(0, _nextTriggerElapsedMilliseconds - _watch.ElapsedMilliseconds);
        }
    }
}