using System.Diagnostics;

namespace CA_DataUploaderLib.Helpers
{
    public class TimeFrequencyThrottle
    {
        private readonly Stopwatch _watch = new Stopwatch();
        private readonly int _timePeriod;
        public TimeFrequencyThrottle(int timePeriodInMilliseconds) => _timePeriod = timePeriodInMilliseconds;
        public bool ShouldRun() => !_watch.IsRunning || _watch.ElapsedMilliseconds > _timePeriod;
        public void FinishedLastRun() => _watch.Restart();
    }
}