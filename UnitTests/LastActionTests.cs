using CA_DataUploaderLib;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Threading.Tasks;

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
        public async Task ClockTimeExpirationTest()
        {
            var now = DateTime.UtcNow;
            var action = new LastAction(0.1, 100);
            Assert.IsTrue(action.ChangedOrExpired(0.1, now), "expiration is expected before executing the first action");
            action.ExecutedNewAction(0.2, now);
            Assert.IsFalse(action.ChangedOrExpired(0.2, now), "no expiration expected at 0s");
            await Task.Delay(60);
            Assert.IsFalse(action.ChangedOrExpired(0.2, now), "no expiration expected at 0.06s clock time");
            await Task.Delay(60);
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
    }
}