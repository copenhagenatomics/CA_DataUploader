using System;
using System.Collections.Generic;
using System.Linq;

namespace CA_DataUploaderLib
{
    public class LastAction
    {
        private readonly int repeatMilliseconds;
        private readonly TimeProvider timeProvider;
        private long lastActionExecutedTime;

        public LastAction(int targetIndex, int repeatMilliseconds) : this(targetIndex, repeatMilliseconds, TimeProvider.System) { }
        public LastAction(int targetIndex, int repeatMilliseconds, TimeProvider timeProvider) : this([targetIndex], repeatMilliseconds, timeProvider) { }
        public LastAction(IEnumerable<int> targetIndices, int repeatMilliseconds) : this(targetIndices, repeatMilliseconds, TimeProvider.System) { }
        public LastAction(IEnumerable<int> targetIndices, int repeatMilliseconds, TimeProvider timeProvider)
        {
            Indices = [.. targetIndices];
            this.repeatMilliseconds = repeatMilliseconds;
            this.timeProvider = timeProvider;
        }

        private double[] Vector { get; set; } = [];
        private int[] Indices { get; set; }
        private DateTime TimeToRepeat { get; set; }

        public IEnumerable<double> Targets => Indices.Select(i => Vector[i]);

        /// <remarks>
        /// We determine whether the last action has expired (should be repeated) by checking the time passed in 2 different ways,
        /// one based purely on the vector times and another based on the local system time. We do this to avoid the related 
        /// actuator from stopping if either of these 2 mechanisms and related fields are affected by a radiation caused bit flip.
        /// Note however that there are many other ways that bit flips could affect related actuations and also the functioning of this very class.
        /// </remarks>
        public bool ChangedOrExpired(double[] newVector, DateTime currentVectorTime) =>
            Indices.Any(i => Vector.Length == 0 || Vector[i] != newVector[i]) || (repeatMilliseconds > -1 && timeProvider.GetElapsedTime(lastActionExecutedTime).TotalMilliseconds >= repeatMilliseconds) || currentVectorTime >= TimeToRepeat;

        public void ExecutedNewAction(double[] newVector, DateTime currentVectorTime)
        {
            Vector = newVector;
            TimeToRepeat = repeatMilliseconds > -1 ? currentVectorTime.AddMilliseconds(repeatMilliseconds) : DateTime.MaxValue;
            lastActionExecutedTime = timeProvider.GetTimestamp();
        }

        public void TimedOutWaitingForDecision()
        {
            //DateTime.MinValue forces execution on the next vector / also note we don't restart time running for the same reason.
            TimeToRepeat = DateTime.MinValue;
        }

        /// <remarks>This method is only for unit testing purposes</remarks>
        public void ResetVectorBasedTimeout(DateTime currentVectorTime) => TimeToRepeat = repeatMilliseconds > -1 ? currentVectorTime.AddMilliseconds(repeatMilliseconds) : DateTime.MaxValue;
    }
}
