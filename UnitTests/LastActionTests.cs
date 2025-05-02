using CA_DataUploaderLib;
using Microsoft.Extensions.Time.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace UnitTests
{
    [TestClass()]
    public class LastActionTests
    {
        [TestMethod()]
        public void VectorOnlyChangeAndExpirationTest()
        {
            var now = DateTime.UtcNow;
            var action = new LastAction(0.1, 500);
            Assert.IsTrue(action.ChangedOrExpired(0.1, now), "expiration is expected before executing the first action");
            now = now.AddSeconds(1);
            action.ExecutedNewAction(0.2, now);
            Assert.IsFalse(action.ChangedOrExpired(0.2, now), "2: no expiration expected at 0s");
            Assert.IsFalse(action.ChangedOrExpired(0.2, now.AddMilliseconds(500).AddTicks(-1)), "2: no expiration expected at 0.5s - 1 tick");
            Assert.IsTrue(action.ChangedOrExpired(0.2, now.AddMilliseconds(500)), "2: expiration expected at 0.5s");
            Assert.IsTrue(action.ChangedOrExpired(0.3, now.AddMilliseconds(100)), "2: target change must be detected");
            action.ExecutedNewAction(0.3, now);
            Assert.IsFalse(action.ChangedOrExpired(0.3, now), "3: no expiration expected at 0s");
            Assert.IsFalse(action.ChangedOrExpired(0.3, now.AddMilliseconds(500).AddTicks(-1)), "3: no expiration expected at 0.5s - 1 tick");
            Assert.IsTrue(action.ChangedOrExpired(0.3, now.AddMilliseconds(500)), "3: expiration expected at 0.5s");
            Assert.IsTrue(action.ChangedOrExpired(0.4, now.AddMilliseconds(100)), "3: target change must be detected");
        }

        [TestMethod()]
        public void ClockTimeExpirationTest()
        {
            var now = DateTime.UtcNow;
            var time = new FakeTimeProvider();
            var action = new LastAction(0.1, 100, time);
            Assert.IsTrue(action.ChangedOrExpired(0.1, now), "expiration is expected before executing the first action");
            action.ExecutedNewAction(0.2, now);
            Assert.IsFalse(action.ChangedOrExpired(0.2, now), "no expiration expected at 0s");
            time.Advance(TimeSpan.FromMilliseconds(60));
            Assert.IsFalse(action.ChangedOrExpired(0.2, now), "no expiration expected at 0.06s clock time");
            time.Advance(TimeSpan.FromMilliseconds(60));
            Assert.IsTrue(action.ChangedOrExpired(0.2, now), "expiration expected at 0.12s clock time");
        }

        [TestMethod()]
        public void VectorOnlyNoRepeatTest()
        {
            var now = DateTime.UtcNow;
            var action = new LastAction(0.1, -1);
            Assert.IsTrue(action.ChangedOrExpired(0.1, now), "expiration is expected before executing the first action");
            now = now.AddSeconds(1);
            action.ExecutedNewAction(0.2, now);
            Assert.IsFalse(action.ChangedOrExpired(0.2, now), "2: no expiration expected at 0s");
            Assert.IsFalse(action.ChangedOrExpired(0.2, now.AddYears(10)), "2: no expiration expected at 10 years");
            Assert.IsTrue(action.ChangedOrExpired(0.3, now.AddMilliseconds(100)), "2: target change must be detected");
            action.ExecutedNewAction(0.3, now);
            Assert.IsFalse(action.ChangedOrExpired(0.3, now), "3: no expiration expected at 0s");
            Assert.IsFalse(action.ChangedOrExpired(0.3, now.AddDays(30)), "3: no expiration expected at 30 days");
            Assert.IsTrue(action.ChangedOrExpired(0.4, now.AddMilliseconds(100)), "3: target change must be detected");
        }

        [TestMethod()]
        public void VectorOnlyMultipleTargetsNoRepeatTest()
        {
            var now = DateTime.UtcNow;
            var action = new LastAction([0.1, 0.2, 0.3], -1);
            Assert.IsTrue(action.ChangedOrExpired([0.1, 0.2, 0.3], now), "expiration is expected before executing the first action");
            now = now.AddSeconds(1);
            action.ExecutedNewAction([0.2, 0.3, 0.4], now);
            Assert.IsFalse(action.ChangedOrExpired([0.2, 0.3, 0.4], now), "2: no expiration expected at 0s");
            Assert.IsFalse(action.ChangedOrExpired([0.2, 0.3, 0.4], now.AddYears(10)), "2: no expiration expected at 10 years");
            Assert.IsTrue(action.ChangedOrExpired([0.3, 0.4, 0.5], now.AddMilliseconds(100)), "2: target change must be detected");
            action.ExecutedNewAction([0.3, 0.4, 0.5], now);
            Assert.IsFalse(action.ChangedOrExpired([0.3, 0.4, 0.5], now), "3: no expiration expected at 0s");
            Assert.IsFalse(action.ChangedOrExpired([0.3, 0.4, 0.5], now.AddDays(30)), "3: no expiration expected at 30 days");
            Assert.IsTrue(action.ChangedOrExpired([0.4, 0.5, 0.6], now.AddMilliseconds(100)), "3: target change must be detected");
        }

        [TestMethod()]
        public void TimedOutWaitingForDecisionTest()
        {
            var now = DateTime.UtcNow;
            var time = new FakeTimeProvider();
            var action = new LastAction(0.1, 500, time);
            Assert.IsTrue(action.ChangedOrExpired(0.1, now), "expiration is expected before executing the first action");
            action.ExecutedNewAction(0.2, now);
            Assert.IsFalse(action.ChangedOrExpired(0.2, now), "no expiration expected at 0s");
            action.TimedOutWaitingForDecision(0);
            Assert.IsTrue(action.ChangedOrExpired(0, now), "expiration expected resuming after a timeout (based on vector date comparisson)");
            action.ResetVectorBasedTimeout(now);//we can only test the time passing after a timeout if we reset the vector based timeout (even passing DateTime.MinValue as now would not skip the vector based comparisson). 
            time.Advance(TimeSpan.FromMilliseconds(500));
            Assert.IsTrue(action.ChangedOrExpired(0, now), "expiration expected resuming after a timeout (based on time passing)");
        }
    }
}