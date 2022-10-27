using System;
using System.Diagnostics;

namespace CA_DataUploaderLib
{
    public class LastAction
    {
        private readonly int repeatMilliseconds;

        public LastAction(double target, int repeatMilliseconds)
        {
            Target = target;
            this.repeatMilliseconds = repeatMilliseconds;
        }

        public double Target { get; private set; } = 0;
        public DateTime TimeToRepeat { get; private set; }
        public Stopwatch TimeRunning { get; } = Stopwatch.StartNew();
        public bool ChangedOrExpired(double newtarget, DateTime currentVectorTime) =>
            Target != newtarget || TimeRunning.ElapsedMilliseconds >= repeatMilliseconds || TimeToRepeat >= currentVectorTime;
        public void ExecutedNewAction(double target, DateTime currentVectorTime)
        {
            Target = target;
            TimeToRepeat = currentVectorTime.AddMilliseconds(repeatMilliseconds);
            TimeRunning.Restart();
        }

        public void TimedOutWaittingForDecision(double target)
        {
            //DateTime.MavValue forces execution on the next vector / also note we don't restart time running for the same reason.
            Target = target;
            TimeToRepeat = DateTime.MaxValue;
        }
    }
}
