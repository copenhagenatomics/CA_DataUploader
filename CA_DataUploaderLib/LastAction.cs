using System;

namespace CA_DataUploaderLib
{
    public class LastAction
    {
        private readonly int repeatMilliseconds;
        private readonly TimeProvider timeProvider;
        private long lastActionExecutedTime;

        public LastAction(double target, int repeatMilliseconds) : this(target, repeatMilliseconds, TimeProvider.System)
        {
            Target = target;
            this.repeatMilliseconds = repeatMilliseconds;
        }

        public LastAction(double target, int repeatMilliseconds, TimeProvider timeProvider)
        {
            Target = target;
            this.repeatMilliseconds = repeatMilliseconds;
            this.timeProvider = timeProvider;
        }

        public double Target { get; private set; } = 0;
        public DateTime TimeToRepeat { get; private set; }
        public bool ChangedOrExpired(double newtarget, DateTime currentVectorTime) =>
            Target != newtarget || (repeatMilliseconds > -1 && timeProvider.GetElapsedTime(lastActionExecutedTime).TotalMilliseconds >= repeatMilliseconds) || currentVectorTime >= TimeToRepeat;
        public void ExecutedNewAction(double target, DateTime currentVectorTime)
        {
            Target = target;
            TimeToRepeat = repeatMilliseconds > -1 ? currentVectorTime.AddMilliseconds(repeatMilliseconds) : DateTime.MaxValue;
            lastActionExecutedTime = timeProvider.GetTimestamp();
        }

        public void TimedOutWaitingForDecision(double target)
        {
            //DateTime.MinValue forces execution on the next vector / also note we don't restart time running for the same reason.
            Target = target;
            TimeToRepeat = DateTime.MinValue;
        }

        /// <remarks>This method is only for unit testing purposes</remarks>
        public void ResetVectorBasedTimeout(DateTime currentVectorTime) => TimeToRepeat = repeatMilliseconds > -1 ? currentVectorTime.AddMilliseconds(repeatMilliseconds) : DateTime.MaxValue;
    }
}
