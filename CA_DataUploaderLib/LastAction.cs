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
            Target != newtarget || (repeatMilliseconds > -1 && TimeRunning.ElapsedMilliseconds >= repeatMilliseconds) || currentVectorTime >= TimeToRepeat;
        public void ExecutedNewAction(double target, DateTime currentVectorTime)
        {
            Target = target;
            TimeToRepeat = repeatMilliseconds > -1 ? currentVectorTime.AddMilliseconds(repeatMilliseconds) : DateTime.MaxValue;
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
