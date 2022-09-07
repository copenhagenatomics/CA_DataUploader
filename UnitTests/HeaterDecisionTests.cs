#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using CA.LoopControlPluginBase;
using CA_DataUploaderLib;
using CA_DataUploaderLib.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace UnitTests
{
    [TestClass]
    public class HeaterDecisionTests
    {
        private CA.LoopControlPluginBase.VectorDescription desc = new(Array.Empty<string>());
        private CA_DataUploaderLib.DataVector vector = new(Array.Empty<double>(), default);
        private List<LoopControlDecision> decisions = new();
        private static HeaterDecisionConfigBuilder NewConfig => new();
        private static Dictionary<string, double> NewVectorSamples => new()
        {
            { "temperature_state", (int)BaseSensorBox.ConnectionState.ReceivingValues },
            { "temp", 44 }
        };
        private ref double Field(string field)
        {
            for (int i = 0; i < desc.Count; i++)
                if (desc[i] == field) return ref vector.Data[i];

            throw new ArgumentOutOfRangeException(nameof(field), $"field not found {field}");
        }
        private void MakeDecisions(string @event, DateTime? time = null) => MakeDecisions(new List<string>() { @event }, time);
        private void MakeDecisions(List<string>? events = null, DateTime? time = null)
        {
            var v = new CA.LoopControlPluginBase.DataVector(time ?? vector.Timestamp, vector.Data);
            foreach (var decision in decisions)
                decision.MakeDecision(v, events ?? new List<string>());
        }
        private void NewHeaterDecisionConfig(HeatingController.HeaterDecision.Config config)
        {
            decisions[1] = new HeatingController.HeaterDecision(config);
            decisions[1].Initialize(desc);
        }

        [TestInitialize]
        public void Setup()
        {
            decisions = new List<LoopControlDecision>() {
                new HeatingController.OvenAreaDecision(new($"ovenarea_0", 0, areasCount: 2)),
                new HeatingController.HeaterDecision(NewConfig.Build())};
            var samples = NewVectorSamples
                .Select(kvp => (field: kvp.Key, value: kvp.Value))
                .Concat(decisions.SelectMany(d => d.PluginFields.Select(f => (field: f.Name, value: 0d))))
                .ToArray();
            desc = new(samples.Select(s => s.field).ToArray());
            foreach (var decision in decisions)
                decision.Initialize(desc);
            vector = new(samples.Select(s => s.value).ToArray(), new DateTime(2020, 06, 22, 2, 2, 2, 100));//starting time is irrelevant as long as time continues moving forward, just some random date here
            MakeDecisions(); //initial round of decisions so that all state machines initialize first
            vector = new(vector.Data, vector.Timestamp.AddMilliseconds(100)); //since we just ran a cycle above, set the time for the next cycle to run
        }

        [TestMethod]
        public void WhenHeaterIsOffCanTurnOn()
        {  
            MakeDecisions("oven 54");
            Assert.AreEqual(1.0, Field("heater_onoff"));
            Assert.AreEqual(vector.Timestamp.AddSeconds(2), Field("heater_controlperiodtimeoff").ToVectorDate());
        }

        [TestMethod]
        public void WhenHeaterIsOverHalfDegreeAboveTempKeepsOff()
        {
            Field("temp") = 70.5;
            MakeDecisions("oven 70");
            Assert.AreEqual(0.0, Field("heater_onoff"));
        }

        [TestMethod]
        public void WhenHeaterIsOnCanTurnOff()
        {
            MakeDecisions("oven 100");
            Field("temp") = 101;
            MakeDecisions(time: vector.Timestamp.AddSeconds(30));
            Assert.AreEqual(0.0, Field("heater_onoff"));
        }

        [TestMethod]
        public void WhenOvenWasTurnedOffAndOnHeaterCanTurnOn()
        {
            MakeDecisions("oven 0");
            MakeDecisions("oven 45", vector.Timestamp.AddMilliseconds(100));
            Assert.AreEqual(1.0, Field("heater_onoff"));
        }

        [TestMethod]
        public void WhenHeaterIsOnCanTurnOffBeforeReachingTargetTemperatureBasedOnProportionalGain()
        {
            //in this test we are 6 degrees below the target temperature when first actuating (so it turns on)
            //and even though the temperature did not change the proportional gain must turn the heater off after the expected amount of time.
            //the gain of 2 means there should pass 2 seconds for every 1C to gain, so we expect it to take 12 seconds before it decides to turn off.
            NewHeaterDecisionConfig(NewConfig.WithProportionalGain(2).Build());
            MakeDecisions("oven 50");
            MakeDecisions(time: vector.Timestamp.AddSeconds(5));
            Assert.AreEqual(1.0, Field("heater_onoff"), "should still be on after 5 seconds");
            MakeDecisions(time: vector.Timestamp.AddSeconds(11));
            Assert.AreEqual(1.0, Field("heater_onoff"), "should still be on after 11 seconds");
            MakeDecisions(time: vector.Timestamp.AddSeconds(12));
            Assert.AreEqual(0.0, Field("heater_onoff"), "should be off after 12 seconds");
            MakeDecisions(time: vector.Timestamp.AddSeconds(29));
            Assert.AreEqual(0.0, Field("heater_onoff"), "should remain off just before next control period (29 seconds)");
            MakeDecisions(time: vector.Timestamp.AddSeconds(30));
            Assert.AreEqual(1.0, Field("heater_onoff"), "should try heating again 30 seconds after it turned off");
        }

        [TestMethod]
        public void ProportionalGainUsesMaxOutput()
        {
            //In this test we are 500 degrees below the target temperature so that it will actuate at max output for a long time.
            //Because we are using time based power control, this means it should turn on for 80% of the control period
            //the gain of 2 means there should pass 2 seconds for every 1C to gain, so the proportional control alone would normally turn it on for the whole control period.
            NewHeaterDecisionConfig(NewConfig.WithProportionalGain(2).WithMaxOutput(0.8d).Build());
            MakeDecisions("oven 544");
            MakeDecisions(time: vector.Timestamp.AddSeconds(5));
            Assert.AreEqual(1.0, Field("heater_onoff"), "should still be on after 5 seconds");
            MakeDecisions(time: vector.Timestamp.AddSeconds(23));
            Assert.AreEqual(1.0, Field("heater_onoff"), "should still be on after 23 seconds");
            MakeDecisions(time: vector.Timestamp.AddSeconds(24));
            Assert.AreEqual(0.0, Field("heater_onoff"), "should be off after 24 seconds");
            MakeDecisions(time: vector.Timestamp.AddSeconds(29));
            Assert.AreEqual(0.0, Field("heater_onoff"), "should remain off just before next control period (29 seconds)");
            MakeDecisions(time: vector.Timestamp.AddSeconds(30));
            Assert.AreEqual(1.0, Field("heater_onoff"), "should try heating again 30 seconds after it turned off");
        }

        [TestMethod]
        public void DisconnectedTemperatureBoardTurnsOffHeaterAfterControlPeriod()
        {
            MakeDecisions("oven 200");
            Assert.AreEqual(1.0, Field("heater_onoff"), "should be on after oven 70");
            Field("temperature_state") = (int)BaseSensorBox.ConnectionState.Connecting;
            MakeDecisions(time: vector.Timestamp.AddSeconds(1));
            Assert.AreEqual(1.0, Field("heater_onoff"), "should still be on when the temperature board is disconnected");
            MakeDecisions(time: vector.Timestamp.AddSeconds(2));
            Assert.AreEqual(1.0, Field("heater_onoff"), "should still be on 1 second after the temperature board is disconnected");
            MakeDecisions(time: vector.Timestamp.AddSeconds(4));
            Assert.AreEqual(1.0, Field("heater_onoff"), "should still be on 3 seconds after the temperature board is disconnected");
            MakeDecisions(time: vector.Timestamp.AddSeconds(29));
            Assert.AreEqual(1.0, Field("heater_onoff"), "should still be on just before next control period (29 seconds)");
            MakeDecisions(time: vector.Timestamp.AddSeconds(30));
            Assert.AreEqual(0.0, Field("heater_onoff"), "should be off on the next control period after the temperature board is disconnected");
            MakeDecisions(time: vector.Timestamp.AddSeconds(31));
            Assert.AreEqual(0.0, Field("heater_onoff"), "should remain off as long as the temperature board is disconnected");
        }

        [TestMethod]
        public void ReconnectedUnderTargetTemperatureStartsPostponedControlPeriodInmediately()
        {
            Field("temperature_state") = (int)BaseSensorBox.ConnectionState.Connecting;
            MakeDecisions("oven 200");
            Assert.AreEqual(0.0, Field("heater_onoff"), "no actuation yet, we are not connected");
            MakeDecisions(time: vector.Timestamp.AddSeconds(2));
            Assert.AreEqual(0.0, Field("heater_onoff"), "still no actuation as we are not connected");
            Field("temperature_state") = (int)BaseSensorBox.ConnectionState.ReceivingValues;
            MakeDecisions(time: vector.Timestamp.AddSeconds(3));
            Assert.AreEqual(1.0, Field("heater_onoff"), "we reconnected, so start the postponed control period right away");
        }

        [TestMethod]
        public void ReconnectedOverTargetTemperatureDoesNotTurnOffUntilNextControlPeriod()
        {
            MakeDecisions("oven 200");
            Assert.AreEqual(1.0, Field("heater_onoff"), "heater must be on before the disconnect");
            Field("temperature_state") = (int)BaseSensorBox.ConnectionState.Connecting;
            MakeDecisions(time: vector.Timestamp.AddSeconds(1));
            Assert.AreEqual(1.0, Field("heater_onoff"), "heater must still be on on the first disconnected cycle");
            Field("temperature_state") = (int)BaseSensorBox.ConnectionState.ReceivingValues;
            Field("temp") = 201;
            MakeDecisions(time: vector.Timestamp.AddSeconds(3));
            Assert.AreEqual(1.0, Field("heater_onoff"), "heater must not be turned off before the end of the control period");
            MakeDecisions(time: vector.Timestamp.AddSeconds(30));
            Assert.AreEqual(0.0, Field("heater_onoff"), "heater must be turned off on the next control period");
        }

        [TestMethod]
        //Important: at the time of writing, even though the oven target can be changed by other plugins, only the oven command currently applies it within the same control period
        public void StateReflectLatestChangesDoneWithTheOvenCommand() 
        {
            MakeDecisions("oven 200");
            Assert.AreEqual(200, Field("ovenarea_0"));
            Assert.AreEqual(HeatingController.HeaterDecision.States.InControlPeriod, (HeatingController.HeaterDecision.States)Field("state_heater"));
            Assert.AreEqual(vector.Timestamp.AddSeconds(30), Field("heater_nextcontrolperiod").ToVectorDate());
            Assert.AreEqual(vector.Timestamp.AddSeconds(30), Field("heater_controlperiodtimeoff").ToVectorDate());
            MakeDecisions("oven 94", vector.Timestamp.AddSeconds(1));
            Assert.AreEqual(94, Field("ovenarea_0"));
            Assert.AreEqual(HeatingController.HeaterDecision.States.InControlPeriod, (HeatingController.HeaterDecision.States)Field("state_heater"));
            Assert.AreEqual(vector.Timestamp.AddSeconds(30), Field("heater_nextcontrolperiod").ToVectorDate());
            Assert.AreEqual(vector.Timestamp.AddSeconds(10), Field("heater_controlperiodtimeoff").ToVectorDate(), "time off not as expected after a temp change (10 seconds after the cycle started)");
            Field("temp") = 150; //setting temp higher than last target to ensure we show the heater stays on regardless
            MakeDecisions("heater heater on", time: vector.Timestamp.AddSeconds(31));
            Assert.AreEqual(94, Field("ovenarea_0"), "target unexpectedly changed when specifying manual mode");
            Assert.AreEqual(HeatingController.HeaterDecision.States.ManualOn, (HeatingController.HeaterDecision.States)Field("state_heater"), "manual on not reflected on state");
            Assert.AreEqual(1.0, Field("heater_onoff"), "heater not on on manual mode");
            MakeDecisions("heater heater off", time: vector.Timestamp.AddSeconds(32));
            Assert.AreEqual(HeatingController.HeaterDecision.States.InControlPeriod, (HeatingController.HeaterDecision.States)Field("state_heater"), "manual off not reflected on state");
            Assert.AreEqual(0.0, Field("heater_onoff"), "heater not off after shutting off manual mode");
        }

        [TestMethod]
        //Important: at the time of writing, even though the oven target can be changed by other plugins, only the oven command currently applies it within the same control period
        public void WhenTargetIsChangedActuatesInmediately()
        {
            MakeDecisions("oven 40");
            MakeDecisions("oven 100", vector.Timestamp.AddSeconds(1));
            Assert.AreEqual(1.0, Field("heater_onoff"));
            MakeDecisions("oven 40", vector.Timestamp.AddSeconds(2));
            Assert.AreEqual(0.0, Field("heater_onoff"));
        }

        [TestMethod]
        public void CanRunWithoutOven()
        {
            NewHeaterDecisionConfig(NewConfig.WithoutOven().Build());
            MakeDecisions();
            Assert.AreEqual(0.0, Field("heater_onoff"));
            MakeDecisions("heater heater on", vector.Timestamp.AddSeconds(5));
            Assert.AreEqual(1.0, Field("heater_onoff"));
            MakeDecisions("heater heater off", vector.Timestamp.AddSeconds(10));
            Assert.AreEqual(0.0, Field("heater_onoff"), "should be off after turning off again");
        }

        [TestMethod]
        public void CanEmergencyShutdownWithoutOven()
        {
            NewHeaterDecisionConfig(NewConfig.WithoutOven().Build());
            MakeDecisions("heater heater on", vector.Timestamp.AddSeconds(5));
            Assert.AreEqual(1.0, Field("heater_onoff"));
            MakeDecisions("emergencyshutdown", vector.Timestamp.AddSeconds(10));
            Assert.AreEqual(0.0, Field("heater_onoff"));
        }

        private class HeaterDecisionConfigBuilder
        {
            private double _proportionalGain = 0.2d;
            private double _maxOutput = 1d;
            private bool _ovenDisabled = false;

            public HeaterDecisionConfigBuilder WithProportionalGain(double value)
            { 
                _proportionalGain = value;
                return this;
            }

            public HeaterDecisionConfigBuilder WithMaxOutput(double value)
            {
                _maxOutput = value;
                return this;
            }

            public HeaterDecisionConfigBuilder WithoutOven()
            {
                _ovenDisabled = true;
                return this;
            }

            public HeatingController.HeaterDecision.Config Build() => _ovenDisabled
                ? new("heater", new List<string>().AsReadOnly())
                : new("heater", new List<string>() { "temperature_state" }.AsReadOnly())
                {
                    ProportionalGain = _proportionalGain,
                    ControlPeriod = TimeSpan.FromSeconds(30),
                    Area = 0,
                    MaxTemperature = 800,
                    OvenSensor = "temp",
                    MaxOutputPercentage = _maxOutput,
                };
        }
    }
}