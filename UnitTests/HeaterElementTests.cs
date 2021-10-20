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
        private HeaterElementConfigBuilder NewConfig => new HeaterElementConfigBuilder();
        private Dictionary<string, double> NewVectorSamples => new Dictionary<string, double>
        {
            {"box_state", (int)BaseSensorBox.ConnectionState.ReceivingValues},
            {"temperature_state", (int)BaseSensorBox.ConnectionState.ReceivingValues},
            {"heater_current", 0.1},
            {"temp", 44},
            {"heater_switchon", 0},
            //we use a time in the future, because HeaterElement does not allow to turn on for the first 20 seconds.
            {"vectortime", new DateTime(2030, 06, 22, 2, 2, 2, 100).ToVectorDouble()}
        };

        [TestMethod]
        public void WhenHeaterIsOffCanTurnOn()
        { 
            var element = new HeaterElement(NewConfig.Build());
            element.SetTargetTemperature(45);
            NewVectorReceivedArgs vector = new NewVectorReceivedArgs(NewVectorSamples);
            var action = element.MakeNextActionDecision(vector);
            Assert.AreEqual(true, action.IsOn);
            Assert.AreEqual(vector.GetVectorTime().AddSeconds(10), action.TimeToTurnOff);
        }

        [TestMethod]
        public void WhenHeaterIsOverHalfDegreeAboveTempKeepsOff()
        { 
            var element = new HeaterElement(NewConfig.Build());
            element.SetTargetTemperature(45);
            var samples = NewVectorSamples;
            samples["temp"] = 45.5;
            NewVectorReceivedArgs vector = new NewVectorReceivedArgs(samples);
            var action = element.MakeNextActionDecision(vector);
            Assert.AreEqual(false, action.IsOn);
        }

        [TestMethod]
        public void WhenHeaterIsOnCanTurnOff()
        { 
            var element = new HeaterElement(NewConfig.Build());
            element.SetTargetTemperature(45);
            NewVectorReceivedArgs vector = new NewVectorReceivedArgs(NewVectorSamples);
            element.MakeNextActionDecision(vector);
            var newSamples = NewVectorSamples;
            newSamples["vectortime"] = vector.GetVectorTime().AddSeconds(10).ToVectorDouble();
            newSamples["temp"] = 46;
            var action = element.MakeNextActionDecision(new NewVectorReceivedArgs(newSamples)); 
            Assert.AreEqual(false, action.IsOn);
        }

        [TestMethod]
        public void WhenOvenWasTurnedOffAndOnHeaterCanTurnOn()
        { 
            var element = new HeaterElement(NewConfig.Build());
            element.SetTargetTemperature(0);
            element.SetTargetTemperature(45);
            NewVectorReceivedArgs vector = new NewVectorReceivedArgs(NewVectorSamples);
            var action = element.MakeNextActionDecision(vector); 
            Assert.AreEqual(true, action.IsOn);
        }

        [TestMethod]
        public void WhenHeaterIsOnCanTurnOffBeforeReachingTargetTemperatureBasedOnProportionalGain()
        {
            //in this test we are 6 degrees below the target temperature when first actuating (so when it turns on)
            //and even though the temperature did not change the proportional gain must turn the heater off after the expected amount of time.
            //the gain of 2 means there should pass 2 seconds for every 1C to gain, so we expect it to take 12 seconds before it decides to turn off.
            var element = new HeaterElement(NewConfig.WithProportionalGain(2).Build());
            element.SetTargetTemperature(50);
            NewVectorReceivedArgs vector = new NewVectorReceivedArgs(NewVectorSamples);
            element.MakeNextActionDecision(vector);
            var newSamples = NewVectorSamples;
            newSamples["vectortime"] = vector.GetVectorTime().AddSeconds(5).ToVectorDouble();
            var action = element.MakeNextActionDecision(new NewVectorReceivedArgs(newSamples));
            Assert.AreEqual(true, action.IsOn, "should still be on after 5 seconds");
            newSamples["vectortime"] = vector.GetVectorTime().AddSeconds(11).ToVectorDouble();
            action = element.MakeNextActionDecision(new NewVectorReceivedArgs(newSamples));
            Assert.AreEqual(true, action.IsOn, "should still be on after 11 seconds");
            newSamples = NewVectorSamples;
            newSamples["vectortime"] = vector.GetVectorTime().AddSeconds(12).ToVectorDouble();
            action = element.MakeNextActionDecision(new NewVectorReceivedArgs(newSamples));
            Assert.AreEqual(false, action.IsOn, "should be off after 12 seconds");
            newSamples = NewVectorSamples;
            newSamples["vectortime"] = vector.GetVectorTime().AddSeconds(17).ToVectorDouble();
            action = element.MakeNextActionDecision(new NewVectorReceivedArgs(newSamples));
            Assert.AreEqual(false, action.IsOn, "should remain off within 10 seconds after turning off / 10 seconds off rule in MustTurnOff");
            newSamples = NewVectorSamples;
            newSamples["vectortime"] = vector.GetVectorTime().AddSeconds(22).ToVectorDouble();
            action = element.MakeNextActionDecision(new NewVectorReceivedArgs(newSamples));
            Assert.AreEqual(true, action.IsOn, "should try heating again 10 seconds after it turned off");
        }

        [TestMethod, Description("avoiding making action changes when we don't have connection to the switchboard anyway")]
        //Note that the switchboard controller would ignore the action regardless of what we send due to the disconnect.
        //However, having the decision logic explicitely indicate there is no action change better reflects the situation.
        public void DisconnectedSwitchBoardDoesNotChangeCurrentAction()
        { 
            var element = new HeaterElement(NewConfig.Build());
            element.SetTargetTemperature(45);
            NewVectorReceivedArgs vector = new NewVectorReceivedArgs(NewVectorSamples);
            var firstAction = element.MakeNextActionDecision(vector);
            var newSamples = NewVectorSamples;
            newSamples["vectortime"] = vector.GetVectorTime().AddSeconds(10).ToVectorDouble();
            newSamples["box_state"] = (int)BaseSensorBox.ConnectionState.Connecting;
            var newAction = element.MakeNextActionDecision(new NewVectorReceivedArgs(newSamples)); 
            Assert.AreEqual(firstAction, newAction);
        }

        [TestMethod]
        public void DisconnectedTemperatureLessThan2SecondsKeepsExistingAction()
        { 
            var element = new HeaterElement(NewConfig.Build());
            element.SetTargetTemperature(45);
            NewVectorReceivedArgs vector = new NewVectorReceivedArgs(NewVectorSamples);
            var firstAction = element.MakeNextActionDecision(vector);
            var newSamples = NewVectorSamples;
            newSamples["vectortime"] = vector.GetVectorTime().AddSeconds(1).ToVectorDouble();
            newSamples["temperature_state"] = (int)BaseSensorBox.ConnectionState.Connecting;
            element.MakeNextActionDecision(new NewVectorReceivedArgs(newSamples)); 
            newSamples = NewVectorSamples;
            newSamples["vectortime"] = vector.GetVectorTime().AddSeconds(2).ToVectorDouble();// only 1 second after detecting the disconnect for the first time
            newSamples["temperature_state"] = (int)BaseSensorBox.ConnectionState.Connecting;
            var newAction = element.MakeNextActionDecision(new NewVectorReceivedArgs(newSamples)); 
            Assert.AreEqual(firstAction, newAction);
        }

        [TestMethod]
        public void DisconnectedTemperatureOven2SecondsTurnsOff()
        { 
            var element = new HeaterElement(NewConfig.Build());
            element.SetTargetTemperature(45);
            NewVectorReceivedArgs vector = new NewVectorReceivedArgs(NewVectorSamples);
            var firstAction = element.MakeNextActionDecision(vector);
            var newSamples = NewVectorSamples;
            newSamples["vectortime"] = vector.GetVectorTime().AddSeconds(1).ToVectorDouble();
            newSamples["temperature_state"] = (int)BaseSensorBox.ConnectionState.Connecting;
            element.MakeNextActionDecision(new NewVectorReceivedArgs(newSamples)); 
            newSamples = NewVectorSamples;
            newSamples["vectortime"] = vector.GetVectorTime().AddSeconds(4).ToVectorDouble();// 3 seconds after detecting the disconnect for the first time
            newSamples["temperature_state"] = (int)BaseSensorBox.ConnectionState.Connecting;
            var newAction = element.MakeNextActionDecision(new NewVectorReceivedArgs(newSamples)); 
            Assert.IsFalse(newAction.IsOn);
            Assert.AreNotEqual(firstAction, newAction);
        }

        [TestMethod]
        public void ReconnectedUnderTargetTemperatureKeepsOnAction()
        { 
            var element = new HeaterElement(NewConfig.Build());
            element.SetTargetTemperature(45);
            NewVectorReceivedArgs vector = new NewVectorReceivedArgs(NewVectorSamples);
            var firstAction = element.MakeNextActionDecision(vector);
            var newSamples = NewVectorSamples;
            newSamples["vectortime"] = vector.GetVectorTime().AddSeconds(1).ToVectorDouble();
            newSamples["temperature_state"] = (int)BaseSensorBox.ConnectionState.Connecting;
            element.MakeNextActionDecision(new NewVectorReceivedArgs(newSamples)); 
            newSamples = NewVectorSamples;
            newSamples["vectortime"] = vector.GetVectorTime().AddSeconds(3).ToVectorDouble();
            newSamples["temperature_state"] = (int)BaseSensorBox.ConnectionState.ReceivingValues;
            var newAction = element.MakeNextActionDecision(new NewVectorReceivedArgs(newSamples)); 
            Assert.IsTrue(newAction.IsOn); 
            Assert.AreEqual(firstAction, newAction);
        }

        [TestMethod]
        public void ReconnectedOverTargetTemperatureTurnsOff()
        { 
            var element = new HeaterElement(NewConfig.Build());
            element.SetTargetTemperature(45);
            NewVectorReceivedArgs vector = new NewVectorReceivedArgs(NewVectorSamples);
            var firstAction = element.MakeNextActionDecision(vector);
            var newSamples = NewVectorSamples;
            newSamples["vectortime"] = vector.GetVectorTime().AddSeconds(1).ToVectorDouble();
            newSamples["temperature_state"] = (int)BaseSensorBox.ConnectionState.Connecting;
            var disconnectedAction = element.MakeNextActionDecision(new NewVectorReceivedArgs(newSamples)); 
            newSamples = NewVectorSamples;
            newSamples["vectortime"] = vector.GetVectorTime().AddSeconds(3).ToVectorDouble();
            newSamples["temperature_state"] = (int)BaseSensorBox.ConnectionState.ReceivingValues;
            newSamples["temp"] = 46;
            var newAction = element.MakeNextActionDecision(new NewVectorReceivedArgs(newSamples)); 
            Assert.IsFalse(newAction.IsOn);
            Assert.AreEqual(firstAction, disconnectedAction, "initial and disconnected action unexpectedly were different");
            Assert.AreNotEqual(disconnectedAction, newAction, "initial and disconnected action unexpectedly were the same");
        }

        private class HeaterElementConfigBuilder
        {
            private double _proportionalGain;

            public HeaterElementConfigBuilder WithProportionalGain(double value)
            { 
                _proportionalGain = value;
                return this;
            }

            public HeaterElement.Config Build() =>
                new HeaterElement.Config
                {
                    ProportionalGain = _proportionalGain,
                    Area = 0,
                    SwitchBoardStateSensorName = "box_state",
                    TemperatureBoardStateSensorNames = new List<string>{ "temperature_state" },
                    CurrentSensingNoiseTreshold = 0.5,
                    CurrentSensorName = "heater_current",
                    MaxTemperature = 800,
                    Name = "heater",
                    OvenSensor = "temp"
                };
        }
    }
}