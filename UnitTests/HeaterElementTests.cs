using System;
using System.Collections.Generic;
using CA.LoopControlPluginBase;
using CA_DataUploaderLib;
using CA_DataUploaderLib.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace UnitTests
{
    [TestClass]
    public class HeaterElementTests
    {
        private static HeaterElementConfigBuilder NewConfig => new();
        private static Dictionary<string, double> NewVectorSamples => new()
        {
            {"temperature_state", (int)BaseSensorBox.ConnectionState.ReceivingValues},
            {"temp", 44},
            {"heater_switchon", 0},
            //we use a time in the future, because HeaterElement does not allow to turn on for the first 20 seconds.
            {"vectortime", new DateTime(2030, 06, 22, 2, 2, 2, 100).ToVectorDouble()}
        };

        [TestMethod]
        public void WhenHeaterIsOffCanTurnOn()
        { 
            var element = new HeaterElement(NewConfig.Build());
            element.SetTargetTemperature(54);
            var vector = new NewVectorReceivedArgs(NewVectorSamples);
            var state = element.GetUpdatedDecisionState(vector);
            Assert.AreEqual(true, state.IsOn);
            Assert.AreEqual(vector.GetVectorTime().AddSeconds(2), state.CurrentControlPeriodTimeOff);
        }

        [TestMethod]
        public void WhenHeaterIsOverHalfDegreeAboveTempKeepsOff()
        { 
            var element = new HeaterElement(NewConfig.Build());
            element.SetTargetTemperature(70);
            var samples = NewVectorSamples;
            samples["temp"] = 70.5;
            var vector = new NewVectorReceivedArgs(samples);
            var state = element.GetUpdatedDecisionState(vector);
            Assert.AreEqual(false, state.IsOn);
        }

        [TestMethod]
        public void WhenHeaterIsOnCanTurnOff()
        { 
            var element = new HeaterElement(NewConfig.Build());
            element.SetTargetTemperature(100);
            var vector = new NewVectorReceivedArgs(NewVectorSamples);
            element.GetUpdatedDecisionState(vector);
            var newSamples = NewVectorSamples;
            newSamples["vectortime"] = vector.GetVectorTime().AddSeconds(30).ToVectorDouble();
            newSamples["temp"] = 101;
            var state = element.GetUpdatedDecisionState(new NewVectorReceivedArgs(newSamples)); 
            Assert.AreEqual(false, state.IsOn);
        }

        [TestMethod]
        public void WhenOvenWasTurnedOffAndOnHeaterCanTurnOn()
        { 
            var element = new HeaterElement(NewConfig.Build());
            element.SetTargetTemperature(0);
            element.SetTargetTemperature(45);
            var vector = new NewVectorReceivedArgs(NewVectorSamples);
            var state = element.GetUpdatedDecisionState(vector); 
            Assert.AreEqual(true, state.IsOn);
        }

        [TestMethod]
        public void WhenHeaterIsOnCanTurnOffBeforeReachingTargetTemperatureBasedOnProportionalGain()
        {
            //in this test we are 6 degrees below the target temperature when first actuating (so it turns on)
            //and even though the temperature did not change the proportional gain must turn the heater off after the expected amount of time.
            //the gain of 2 means there should pass 2 seconds for every 1C to gain, so we expect it to take 12 seconds before it decides to turn off.
            var element = new HeaterElement(NewConfig.WithProportionalGain(2).Build());
            element.SetTargetTemperature(50);
            var vector = new NewVectorReceivedArgs(NewVectorSamples);
            element.GetUpdatedDecisionState(vector);
            var newSamples = NewVectorSamples;
            newSamples["vectortime"] = vector.GetVectorTime().AddSeconds(5).ToVectorDouble();
            var state = element.GetUpdatedDecisionState(new NewVectorReceivedArgs(newSamples));
            Assert.AreEqual(true, state.IsOn, "should still be on after 5 seconds");
            newSamples["vectortime"] = vector.GetVectorTime().AddSeconds(11).ToVectorDouble();
            state = element.GetUpdatedDecisionState(new NewVectorReceivedArgs(newSamples));
            Assert.AreEqual(true, state.IsOn, "should still be on after 11 seconds");
            newSamples = NewVectorSamples;
            newSamples["vectortime"] = vector.GetVectorTime().AddSeconds(12).ToVectorDouble();
            state = element.GetUpdatedDecisionState(new NewVectorReceivedArgs(newSamples));
            Assert.AreEqual(false, state.IsOn, "should be off after 12 seconds");
            newSamples = NewVectorSamples;
            newSamples["vectortime"] = vector.GetVectorTime().AddSeconds(17).ToVectorDouble();
            state = element.GetUpdatedDecisionState(new NewVectorReceivedArgs(newSamples));
            Assert.AreEqual(false, state.IsOn, "should remain off within 30 seconds after turning on");
            newSamples = NewVectorSamples;
            newSamples["vectortime"] = vector.GetVectorTime().AddSeconds(30).ToVectorDouble();
            state = element.GetUpdatedDecisionState(new NewVectorReceivedArgs(newSamples));
            Assert.AreEqual(true, state.IsOn, "should try heating again 30 seconds after it turned off");
        }

        [TestMethod]
        public void ProportionalGainUsesMaxOutput()
        {
            //In this test we are 500 degrees below the target temperature so that it will actuate at max output for a long time.
            //Because we are using time based power control, this means it should turn on for 80% of the control period
            //the gain of 2 means there should pass 2 seconds for every 1C to gain, so the proportional control alone would normally turn it on for the whole control period.
            var element = new HeaterElement(NewConfig.WithProportionalGain(2).WithMaxOutput(0.8d).Build());
            element.SetTargetTemperature(544);
            var vector = new NewVectorReceivedArgs(NewVectorSamples);
            element.GetUpdatedDecisionState(vector);
            var newSamples = NewVectorSamples;
            newSamples["vectortime"] = vector.GetVectorTime().AddSeconds(5).ToVectorDouble();
            var state = element.GetUpdatedDecisionState(new NewVectorReceivedArgs(newSamples));
            Assert.AreEqual(true, state.IsOn, "should still be on after 5 seconds");
            newSamples["vectortime"] = vector.GetVectorTime().AddSeconds(23).ToVectorDouble();
            state = element.GetUpdatedDecisionState(new NewVectorReceivedArgs(newSamples));
            Assert.AreEqual(true, state.IsOn, "should still be on after 23 seconds");
            newSamples = NewVectorSamples;
            newSamples["vectortime"] = vector.GetVectorTime().AddSeconds(24).ToVectorDouble();
            state = element.GetUpdatedDecisionState(new NewVectorReceivedArgs(newSamples));
            Assert.AreEqual(false, state.IsOn, "should be off after 24 seconds");
            newSamples = NewVectorSamples;
            newSamples["vectortime"] = vector.GetVectorTime().AddSeconds(29).ToVectorDouble();
            state = element.GetUpdatedDecisionState(new NewVectorReceivedArgs(newSamples));
            Assert.AreEqual(false, state.IsOn, "should remain off just before next control period (29 seconds)");
            newSamples = NewVectorSamples;
            newSamples["vectortime"] = vector.GetVectorTime().AddSeconds(30).ToVectorDouble();
            state = element.GetUpdatedDecisionState(new NewVectorReceivedArgs(newSamples));
            Assert.AreEqual(true, state.IsOn, "should try heating again on the next control period");
        }

        [TestMethod]
        public void DisconnectedTemperatureLessThan2SecondsKeepsExistingAction()
        { 
            var element = new HeaterElement(NewConfig.Build());
            element.SetTargetTemperature(70);
            var vector = new NewVectorReceivedArgs(NewVectorSamples);
            var state = element.GetUpdatedDecisionState(vector);
            var firstOn = state.IsOn;
            var newSamples = NewVectorSamples;
            newSamples["vectortime"] = vector.GetVectorTime().AddSeconds(1).ToVectorDouble();
            newSamples["temperature_state"] = (int)BaseSensorBox.ConnectionState.Connecting;
            element.GetUpdatedDecisionState(new NewVectorReceivedArgs(newSamples)); 
            newSamples = NewVectorSamples;
            newSamples["vectortime"] = vector.GetVectorTime().AddSeconds(2).ToVectorDouble();// only 1 second after detecting the disconnect for the first time
            newSamples["temperature_state"] = (int)BaseSensorBox.ConnectionState.Connecting;
            state = element.GetUpdatedDecisionState(new NewVectorReceivedArgs(newSamples)); 
            Assert.AreEqual(firstOn, state.IsOn);
        }

        [TestMethod]
        public void DisconnectedTemperatureOver2SecondsKeepsExistingAction()
        { 
            var element = new HeaterElement(NewConfig.Build());
            element.SetTargetTemperature(70);
            var vector = new NewVectorReceivedArgs(NewVectorSamples);
            var state = element.GetUpdatedDecisionState(vector);
            var firstOn = state.IsOn;
            var newSamples = NewVectorSamples;
            newSamples["vectortime"] = vector.GetVectorTime().AddSeconds(1).ToVectorDouble();
            newSamples["temperature_state"] = (int)BaseSensorBox.ConnectionState.Connecting;
            element.GetUpdatedDecisionState(new NewVectorReceivedArgs(newSamples)); 
            newSamples = NewVectorSamples;
            newSamples["vectortime"] = vector.GetVectorTime().AddSeconds(4).ToVectorDouble();// 3 seconds after detecting the disconnect for the first time
            newSamples["temperature_state"] = (int)BaseSensorBox.ConnectionState.Connecting;
            state = element.GetUpdatedDecisionState(new NewVectorReceivedArgs(newSamples)); 
            Assert.AreEqual(firstOn, state.IsOn);
        }

        [TestMethod]
        public void DisconnectedTemperatureOverControlPeriodLengthTurnsOff()
        {
            var element = new HeaterElement(NewConfig.Build());
            element.SetTargetTemperature(200);
            var vector = new NewVectorReceivedArgs(NewVectorSamples);
            element.GetUpdatedDecisionState(vector);
            var newSamples = NewVectorSamples;
            newSamples["vectortime"] = vector.GetVectorTime().AddSeconds(29).ToVectorDouble();
            newSamples["temperature_state"] = (int)BaseSensorBox.ConnectionState.Connecting;
            var state = element.GetUpdatedDecisionState(new NewVectorReceivedArgs(newSamples));
            Assert.IsTrue(state.IsOn);//still within the control period, should not turn off yet
            newSamples = NewVectorSamples;
            newSamples["vectortime"] = vector.GetVectorTime().AddSeconds(30).ToVectorDouble();
            newSamples["temperature_state"] = (int)BaseSensorBox.ConnectionState.Connecting;
            state = element.GetUpdatedDecisionState(new NewVectorReceivedArgs(newSamples));
            Assert.IsFalse(state.IsOn);//reached the end of the control period, so it should turn off
            newSamples = NewVectorSamples;
            newSamples["vectortime"] = vector.GetVectorTime().AddSeconds(31).ToVectorDouble();
            newSamples["temperature_state"] = (int)BaseSensorBox.ConnectionState.Connecting;
            state = element.GetUpdatedDecisionState(new NewVectorReceivedArgs(newSamples));
            Assert.IsFalse(state.IsOn);//must still be off as long as it keep being disconnected (its good to do the extra check, as the first actuation at the end of the control period tends to be off)
        }

        [TestMethod]
        public void ReconnectedUnderTargetTemperatureStartsPostponedControlPeriodInmediately()
        { 
            var element = new HeaterElement(NewConfig.Build());
            element.SetTargetTemperature(200);
            var vector = new NewVectorReceivedArgs(NewVectorSamples);
            var newSamples = NewVectorSamples;
            newSamples["vectortime"] = vector.GetVectorTime().ToVectorDouble();
            newSamples["temperature_state"] = (int)BaseSensorBox.ConnectionState.Connecting;
            var state = element.GetUpdatedDecisionState(new NewVectorReceivedArgs(newSamples));
            Assert.IsFalse(state.IsOn); //no actuation yet, we are not connected
            newSamples = NewVectorSamples;
            newSamples["vectortime"] = vector.GetVectorTime().AddSeconds(2).ToVectorDouble();
            newSamples["temperature_state"] = (int)BaseSensorBox.ConnectionState.Connecting;
            state = element.GetUpdatedDecisionState(new NewVectorReceivedArgs(newSamples));
            Assert.IsFalse(state.IsOn); //still no actuation as we are not connected
            newSamples = NewVectorSamples;
            newSamples["vectortime"] = vector.GetVectorTime().AddSeconds(3).ToVectorDouble();
            newSamples["temperature_state"] = (int)BaseSensorBox.ConnectionState.ReceivingValues;
            state = element.GetUpdatedDecisionState(new NewVectorReceivedArgs(newSamples));
            Assert.IsTrue(state.IsOn); //we reconnected, so start the postponed control period right away
        }

        [TestMethod]
        public void ReconnectedOverTargetTemperatureDoesNotTurnOffUntilNextControlPeriod()
        { 
            var element = new HeaterElement(NewConfig.Build());
            element.SetTargetTemperature(200);
            var vector = new NewVectorReceivedArgs(NewVectorSamples);
            Assert.IsTrue(element.GetUpdatedDecisionState(vector).IsOn, "heater was already off before the disconnected");
            var newSamples = NewVectorSamples;
            newSamples["vectortime"] = vector.GetVectorTime().AddSeconds(1).ToVectorDouble();
            newSamples["temperature_state"] = (int)BaseSensorBox.ConnectionState.Connecting;
            Assert.IsTrue(element.GetUpdatedDecisionState(new NewVectorReceivedArgs(newSamples)).IsOn, "heater was turned off on the first disconnected cycle");
            newSamples = NewVectorSamples;
            newSamples["vectortime"] = vector.GetVectorTime().AddSeconds(3).ToVectorDouble();
            newSamples["temperature_state"] = (int)BaseSensorBox.ConnectionState.ReceivingValues;
            newSamples["temp"] = 201;
            Assert.IsTrue(element.GetUpdatedDecisionState(new NewVectorReceivedArgs(newSamples)).IsOn, "heater was turned off before the end of the control period");
            newSamples["vectortime"] = vector.GetVectorTime().AddSeconds(30).ToVectorDouble();
            var nextControlPeriodState = element.GetUpdatedDecisionState(new NewVectorReceivedArgs(newSamples));
            Assert.IsFalse(nextControlPeriodState.IsOn, "heater was still on on the next control period");
        }

        [TestMethod]
        public void StateReflectLatestChanges()
        {
            var element = new HeaterElement(NewConfig.Build());
            element.SetTargetTemperature(200);
            var vector = new NewVectorReceivedArgs(NewVectorSamples);
            var state = element.GetUpdatedDecisionState(vector);
            Assert.AreEqual(200, state.Target);
            Assert.IsFalse(state.ManualOn, "manual unexpectedly on");
            Assert.AreEqual(vector.GetVectorTime(), state.CurrentControlPeriodStart);
            Assert.AreEqual(vector.GetVectorTime().AddSeconds(30), state.CurrentControlPeriodTimeOff);
            var newSamples = NewVectorSamples;
            newSamples["vectortime"] = vector.GetVectorTime().AddSeconds(1).ToVectorDouble();
            element.SetTargetTemperature(94);
            state = element.GetUpdatedDecisionState(new (newSamples));
            Assert.AreEqual(94, state.Target);
            Assert.AreEqual(vector.GetVectorTime().AddSeconds(1), state.CurrentControlPeriodStart, "control period not restarted after a temp change");
            Assert.AreEqual(vector.GetVectorTime().AddSeconds(11), state.CurrentControlPeriodTimeOff, "control period time off not as expected after a temp change");
            newSamples = NewVectorSamples;
            newSamples["vectortime"] = vector.GetVectorTime().AddSeconds(31).ToVectorDouble();
            newSamples["temp"] = 150; //setting temp higher than last target to ensure we show the heater stays on regardless
            element.SetManualMode(true);
            state = element.GetUpdatedDecisionState(new(newSamples));
            Assert.AreEqual(94, state.Target, "target unexpectedly changed when specifying manual mode");
            Assert.IsTrue(state.ManualOn, "manual on not reflected on state");
            Assert.IsTrue(state.IsOn, "heater not on on manual mode");
            newSamples = NewVectorSamples;
            newSamples["vectortime"] = vector.GetVectorTime().AddSeconds(32).ToVectorDouble();
            newSamples["temp"] = 150; //still keeping the temp high as manual mode off means doing a regular cycle again
            element.SetManualMode(false);
            state = element.GetUpdatedDecisionState(new(newSamples));
            Assert.IsFalse(state.ManualOn, "manual off not reflected on state");
            Assert.IsFalse(state.IsOn, "heater not off after shutting off manual mode");
        }

        [TestMethod]
        public void WhenTargetIsChangedActuatesInmediately()
        {
            var element = new HeaterElement(NewConfig.Build());
            element.SetTargetTemperature(40);
            var vector = new NewVectorReceivedArgs(NewVectorSamples);
            element.GetUpdatedDecisionState(vector);
            var newSamples = NewVectorSamples;
            newSamples["vectortime"] = vector.GetVectorTime().AddSeconds(1).ToVectorDouble();
            element.SetTargetTemperature(100);
            var state = element.GetUpdatedDecisionState(new(newSamples));
            Assert.IsTrue(state.IsOn);
            newSamples = NewVectorSamples;
            newSamples["vectortime"] = vector.GetVectorTime().AddSeconds(2).ToVectorDouble();
            element.SetTargetTemperature(40);
            state = element.GetUpdatedDecisionState(new(newSamples));
            Assert.IsFalse(state.IsOn);
        }

        private class HeaterElementConfigBuilder
        {
            private double _proportionalGain = 0.2d;
            private double _maxOutput = 1d;

            public HeaterElementConfigBuilder WithProportionalGain(double value)
            { 
                _proportionalGain = value;
                return this;
            }

            public HeaterElementConfigBuilder WithMaxOutput(double value)
            {
                _maxOutput = value;
                return this;
            }

            public HeaterElement.Config Build() =>
                new()
                {
                    ProportionalGain = _proportionalGain,
                    ControlPeriod = TimeSpan.FromSeconds(30),
                    Area = 0,
                    TemperatureBoardStateSensorNames = new List<string>{ "temperature_state" },
                    MaxTemperature = 800,
                    Name = "heater",
                    OvenSensor = "temp",
                    MaxOutputPercentage = _maxOutput,
                };
        }
    }
}