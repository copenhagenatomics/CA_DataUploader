using System;
using System.Linq;
using CA.LoopControlPluginBase;
using CA_DataUploaderLib;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace UnitTests
{
    [TestClass]
    public class SwitchboardActionTests
    {
        [TestMethod]
        public void RepeatMaxReturnsModifiedAction()
        {
            var action = new SwitchboardAction(true, DateTime.MaxValue);
            var repeatAction = action.Repeat(DateTime.UtcNow);
            Assert.AreNotEqual(action, repeatAction);
        }

        [TestMethod]
        public void RemainingSecondsOnRepeatActionReturnsIntMax()
        {
            var remaining = new SwitchboardAction(true, DateTime.MaxValue)
                .Repeat(DateTime.UtcNow)
                .GetRemainingOnSeconds(DateTime.UtcNow);
            Assert.AreEqual(int.MaxValue, remaining);
        }

        [TestMethod]
        public void RemainingSecondsOnRepeatActionViaVectorReturnsIntMax()
        {
            var samples = new SwitchboardAction(true, DateTime.MaxValue)
                .Repeat(DateTime.UtcNow)
                .ToVectorSamples("port", DateTime.UtcNow);
            var actionFromVector = SwitchboardAction.FromVectorSamples(
                new NewVectorReceivedArgs(samples.ToDictionary(s => s.Name, s => s.Value)), "port");
            var remaining = actionFromVector.GetRemainingOnSeconds(DateTime.UtcNow);
            Assert.AreEqual(int.MaxValue, remaining);
        }
                
        [TestMethod]
        public void RemainingSecondsOnMaxViaVectorReturnsIntMax()
        {
            var samples = new SwitchboardAction(true, DateTime.MaxValue).ToVectorSamples("port", DateTime.UtcNow);
            var actionFromVector = SwitchboardAction.FromVectorSamples(
                new NewVectorReceivedArgs(samples.ToDictionary(s => s.Name, s => s.Value)), "port");
            var remaining = actionFromVector.GetRemainingOnSeconds(DateTime.UtcNow);
            Assert.AreEqual(int.MaxValue, remaining);
        }

        [TestMethod]
        public void TimeToTurnOffOnRepeatActionViaVectorReturnsDateTimeMax()
        {
            var samples = new SwitchboardAction(true, DateTime.MaxValue)
                .Repeat(DateTime.UtcNow)
                .ToVectorSamples("port", DateTime.UtcNow);
            var actionFromVector = SwitchboardAction.FromVectorSamples(
                new NewVectorReceivedArgs(samples.ToDictionary(s => s.Name, s => s.Value)), "port");
            Assert.AreEqual(DateTime.MaxValue, actionFromVector.TimeToTurnOff);
        }
                
        [TestMethod]
        public void TimeToTurnOffOnMaxViaVectorReturnsDateTimeMax()
        {
            var samples = new SwitchboardAction(true, DateTime.MaxValue).ToVectorSamples("port", DateTime.UtcNow);
            var actionFromVector = SwitchboardAction.FromVectorSamples(
                new NewVectorReceivedArgs(samples.ToDictionary(s => s.Name, s => s.Value)), "port");
            Assert.AreEqual(DateTime.MaxValue, actionFromVector.TimeToTurnOff);
        }
                
        [TestMethod]
        public void RemainingSecondsViaVectorReturnsIntMax()
        {
            var samples = new SwitchboardAction(true, DateTime.MaxValue)
                .Repeat(DateTime.UtcNow)
                .ToVectorSamples("port", DateTime.UtcNow);
            var actionFromVector = SwitchboardAction.FromVectorSamples(
                new NewVectorReceivedArgs(samples.ToDictionary(s => s.Name, s => s.Value)), "port");
            var remaining = actionFromVector.GetRemainingOnSeconds(DateTime.UtcNow);
            Assert.AreEqual(int.MaxValue, remaining);
        }
                
        [TestMethod]
        public void RemainingSecondsOnTimeWithTicksViaVectorReturnsFullSeconds()
        {
            var vectorTime =  new DateTime(2021, 6, 22, 12, 5, 2, 333).AddTicks(42);
            var samples = new SwitchboardAction(true, vectorTime.AddSeconds(10)).ToVectorSamples("port", vectorTime);
            var actionFromVector = SwitchboardAction.FromVectorSamples(new NewVectorReceivedArgs(samples.ToDictionary(s => s.Name, s => s.Value)), "port");
            var remaining = actionFromVector.GetRemainingOnSeconds(vectorTime);
            Assert.AreEqual(10, remaining);
        }
               
        [TestMethod]
        public void RemainingSecondsOnTimeWithHighTicksViaVectorReturnsFullSeconds()
        {
            var vectorTime =  new DateTime(2021, 6, 22, 12, 5, 2, 333).AddTicks(-1);
            var samples = new SwitchboardAction(true, vectorTime.AddSeconds(10)).ToVectorSamples("port", vectorTime);
            var actionFromVector = SwitchboardAction.FromVectorSamples(new NewVectorReceivedArgs(samples.ToDictionary(s => s.Name, s => s.Value)), "port");
            var remaining = actionFromVector.GetRemainingOnSeconds(vectorTime);
            Assert.AreEqual(10, remaining);
        }
               
        [TestMethod]
        public void RemainingSecondsOnTimeWithLowTicksViaVectorReturnsFullSeconds()
        {
            var vectorTime =  new DateTime(2021, 6, 22, 12, 5, 2, 333).AddTicks(1);
            var samples = new SwitchboardAction(true, vectorTime.AddSeconds(10)).ToVectorSamples("port", vectorTime);
            var actionFromVector = SwitchboardAction.FromVectorSamples(new NewVectorReceivedArgs(samples.ToDictionary(s => s.Name, s => s.Value)), "port");
            var remaining = actionFromVector.GetRemainingOnSeconds(vectorTime);
            Assert.AreEqual(10, remaining);
        }
               
        [TestMethod]
        public void RemainingSecondsAfter5SecondsReturns5Seconds()
        {
            var vectorTime =  new DateTime(2021, 6, 22, 12, 5, 2, 333).AddTicks(1);
            var samples = new SwitchboardAction(true, vectorTime.AddSeconds(10)).ToVectorSamples("port", vectorTime);
            var actionFromVector = SwitchboardAction.FromVectorSamples(new NewVectorReceivedArgs(samples.ToDictionary(s => s.Name, s => s.Value)), "port");
            var remaining = actionFromVector.GetRemainingOnSeconds(vectorTime.AddSeconds(5));
            Assert.AreEqual(5, remaining);
        }
               
        [TestMethod]
        public void RemainingSecondsAfter10SecondsReturns0Seconds()
        {
            var vectorTime =  new DateTime(2021, 6, 22, 12, 5, 2, 333).AddTicks(1);
            var samples = new SwitchboardAction(true, vectorTime.AddSeconds(10)).ToVectorSamples("port", vectorTime);
            var actionFromVector = SwitchboardAction.FromVectorSamples(new NewVectorReceivedArgs(samples.ToDictionary(s => s.Name, s => s.Value)), "port");
            var remaining = actionFromVector.GetRemainingOnSeconds(vectorTime.AddSeconds(10));
            Assert.AreEqual(0, remaining);
        }
               
        [TestMethod]
        public void RemainingSecondsAfter9SecondsAnd800MillisecondsReturns1Second()
        {
            var vectorTime =  new DateTime(2021, 6, 22, 12, 5, 2, 333).AddTicks(1);
            var samples = new SwitchboardAction(true, vectorTime.AddSeconds(10)).ToVectorSamples("port", vectorTime);
            var actionFromVector = SwitchboardAction.FromVectorSamples(new NewVectorReceivedArgs(samples.ToDictionary(s => s.Name, s => s.Value)), "port");
            var remaining = actionFromVector.GetRemainingOnSeconds(vectorTime.AddSeconds(9).AddMilliseconds(800));
            Assert.AreEqual(1, remaining);
        }
    }
}