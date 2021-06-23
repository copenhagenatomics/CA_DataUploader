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
            {"box_state", (int)BaseSensorBox.ConnectionState.Connected},
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

        private class HeaterElementConfigBuilder
        {
            public HeaterElement.Config Build() =>
                new HeaterElement.Config
                {
                    Area = 0,
                    BoardStateSensorName = "box_state",
                    CurrentSensingNoiseTreshold = 0.5,
                    CurrentSensorName = "heater_current",
                    MaxTemperature = 800,
                    Name = "heater",
                    OvenSensor = "temp",
                    SwitchboardOnOffSensorName = "heater_switchon"
                };
        }
    }
}