using System;
using System.Collections.Generic;
using CA_DataUploaderLib;
using CA_DataUploaderLib.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static System.FormattableString;

namespace UnitTests
{
    [TestClass]
    public class HeaterDecisionTests
    {
        FullDecisionTestContext testContext = new("");
        private ref double Field(string field) => ref testContext.Field(field);
        private void MakeDecisions(string? @event = null, double time = 0) => testContext.MakeDecisions(@event, time);
        private void MakeDecisions(List<string> @event, double time = 0) => testContext.MakeDecisions(@event, time);
        private static HeaterDecisionConfigBuilder NewConfig => new();
        private void ReplaceConfig(HeaterDecisionConfigBuilder? config = null, string extraLines = "") => 
            testContext = new(
                @$"{(config ?? NewConfig).Build()}
                OvenArea;2
                {extraLines}",
                new()
                {
                    { "temperature_state", (int)BaseSensorBox.ConnectionState.ReceivingValues },
                    { "temp", 44 }
                });
        [TestInitialize]
        public void Setup() => ReplaceConfig(NewConfig);

        [TestMethod]
        public void RejectsExtraOvenArea() => Assert.Throws<FormatException>(() => ReplaceConfig(extraLines: "OvenArea;2"));
        [TestMethod]
        public void RejectsExtraMixingOvenAreaWithRegularOven() => Assert.Throws<FormatException>(() => ReplaceConfig(extraLines: "Oven;2;heater;temp"));
        [TestMethod]
        public void RejectsExtraOvenForSameHeater() => Assert.Throws<FormatException>(() => ReplaceConfig(extraLines: $"Math;fake;123{Environment.NewLine}Oven;1;heater;fake"));

        [TestMethod]
        public void WhenHeaterIsOffCanTurnOn()
        {
            MakeDecisions("oven 54");
            Assert.AreEqual(1.0, Field("heater_onoff"));
            Assert.AreEqual(testContext.InitialTime.AddSeconds(2), Field("heater_controlperiodtimeoff").ToVectorDate());
        }

        [TestMethod]
        public void WhenHeaterIsOffCanTurnOnWithOvenAreaAll()
        {
            MakeDecisions("ovenarea all 54");
            Assert.AreEqual(1.0, Field("heater_onoff"));
        }

        [TestMethod]
        public void WhenHeaterIsOffCanTurnOnWithOvenArea()
        {
            MakeDecisions("ovenarea 1 54");
            Assert.AreEqual(1.0, Field("heater_onoff"));
        }

        [TestMethod]
        public void WhenHeaterIsOffIgnoresUnrelatedArea()
        {
            MakeDecisions("ovenarea 2 54");
            Assert.AreEqual(0.0, Field("heater_onoff"));
            Assert.AreEqual(54, Field("ovenarea2_target"));
        }

        [TestMethod]
        public void OvenWithMultipleAreasIsRejected()
        {
            MakeDecisions("oven 54 42");
            StringAssert.Contains(testContext.GetAllLogs(), "error-A-Command: oven 54 42 - bad command");
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
            MakeDecisions(time: 30);
            Assert.AreEqual(0.0, Field("heater_onoff"));
        }

        [TestMethod]
        public void WhenOvenWasTurnedOffAndOnHeaterCanTurnOn()
        {
            MakeDecisions("oven 0");
            MakeDecisions("oven 45", time: 0.1);
            Assert.AreEqual(1.0, Field("heater_onoff"));
        }

        [TestMethod]
        public void WhenHeaterIsOnCanTurnOffBeforeReachingTargetTemperatureBasedOnProportionalGain()
        {
            ReplaceConfig(NewConfig.WithProportionalGain(2));
            CheckDecisionsBehaviorMatchesAProportionalGainOf2();
        }

        private void CheckDecisionsBehaviorMatchesAProportionalGainOf2(string initialCommands) => CheckDecisionsBehaviorMatchesAProportionalGainOf2([initialCommands]);
        private void CheckDecisionsBehaviorMatchesAProportionalGainOf2(string[]? initialCommands = null)
        {
            //in this test we are 6 degrees below the target temperature when first actuating (so it turns on)
            //and even though the temperature did not change the proportional gain must turn the heater off after the expected amount of time.
            //the gain of 2 means there should pass 2 seconds for every 1C to gain, so we expect it to take 12 seconds before it decides to turn off.
            var commands = new List<string>() { "oven 50" };
            if (initialCommands != null) 
                commands.InsertRange(0, initialCommands);

            MakeDecisions(commands);
            MakeDecisions(time: 5);
            Assert.AreEqual(1.0, Field("heater_onoff"), "should still be on after 5 seconds");
            MakeDecisions(time: 11);
            Assert.AreEqual(1.0, Field("heater_onoff"), "should still be on after 11 seconds");
            MakeDecisions(time: 12);
            Assert.AreEqual(0.0, Field("heater_onoff"), "should be off after 12 seconds");
            MakeDecisions(time: 29);
            Assert.AreEqual(0.0, Field("heater_onoff"), "should remain off just before next control period (29 seconds)");
            MakeDecisions(time: 30);
            Assert.AreEqual(1.0, Field("heater_onoff"), "should try heating again 30 seconds after it turned off");
        }

        [TestMethod]
        public void ProportionalGainUsesMaxOutput()
        {
            //In this test we are 500 degrees below the target temperature so that it will actuate at max output for a long time.
            //Because we are using time based power control, this means it should turn on for 80% of the control period
            //the gain of 2 means there should pass 2 seconds for every 1C to gain, so the proportional control alone would normally turn it on for the whole control period.
            ReplaceConfig(NewConfig.WithProportionalGain(2).WithMaxOutput(0.8d));
            MakeDecisions("oven 544");
            MakeDecisions(time: 5);
            Assert.AreEqual(1.0, Field("heater_onoff"), "should still be on after 5 seconds");
            MakeDecisions(time: 23);
            Assert.AreEqual(1.0, Field("heater_onoff"), "should still be on after 23 seconds");
            MakeDecisions(time: 24);
            Assert.AreEqual(0.0, Field("heater_onoff"), "should be off after 24 seconds");
            MakeDecisions(time: 29);
            Assert.AreEqual(0.0, Field("heater_onoff"), "should remain off just before next control period (29 seconds)");
            MakeDecisions(time: 30);
            Assert.AreEqual(1.0, Field("heater_onoff"), "should try heating again 30 seconds after it turned off");
        }

        [TestMethod]
        public void DisconnectedTemperatureBoardTurnsOffHeaterAfterControlPeriod()
        {
            MakeDecisions("oven 200");
            Assert.AreEqual(1.0, Field("heater_onoff"), "should be on after oven 70");
            Field("temperature_state") = (int)BaseSensorBox.ConnectionState.Connecting;
            MakeDecisions(time: 1);
            Assert.AreEqual(1.0, Field("heater_onoff"), "should still be on when the temperature board is disconnected");
            MakeDecisions(time: 2);
            Assert.AreEqual(1.0, Field("heater_onoff"), "should still be on 1 second after the temperature board is disconnected");
            MakeDecisions(time: 4);
            Assert.AreEqual(1.0, Field("heater_onoff"), "should still be on 3 seconds after the temperature board is disconnected");
            MakeDecisions(time: 29);
            Assert.AreEqual(1.0, Field("heater_onoff"), "should still be on just before next control period (29 seconds)");
            MakeDecisions(time: 30);
            Assert.AreEqual(0.0, Field("heater_onoff"), "should be off on the next control period after the temperature board is disconnected");
            MakeDecisions(time: 31);
            Assert.AreEqual(0.0, Field("heater_onoff"), "should remain off as long as the temperature board is disconnected");
        }

        [TestMethod]
        public void ReconnectedUnderTargetTemperatureStartsPostponedControlPeriodImmediately()
        {
            Field("temperature_state") = (int)BaseSensorBox.ConnectionState.Connecting;
            MakeDecisions("oven 200");
            Assert.AreEqual(0.0, Field("heater_onoff"), "no actuation yet, we are not connected");
            MakeDecisions(time: 2);
            Assert.AreEqual(0.0, Field("heater_onoff"), "still no actuation as we are not connected");
            Field("temperature_state") = (int)BaseSensorBox.ConnectionState.ReceivingValues;
            MakeDecisions(time: 3);
            Assert.AreEqual(1.0, Field("heater_onoff"), "we reconnected, so start the postponed control period right away");
        }

        [TestMethod]
        public void ReconnectedOverTargetTemperatureDoesNotTurnOffUntilNextControlPeriod()
        {
            MakeDecisions("oven 200");
            Assert.AreEqual(1.0, Field("heater_onoff"), "heater must be on before the disconnect");
            Field("temperature_state") = (int)BaseSensorBox.ConnectionState.Connecting;
            MakeDecisions(time: 1);
            Assert.AreEqual(1.0, Field("heater_onoff"), "heater must still be on on the first disconnected cycle");
            Field("temperature_state") = (int)BaseSensorBox.ConnectionState.ReceivingValues;
            Field("temp") = 201;
            MakeDecisions(time: 3);
            Assert.AreEqual(1.0, Field("heater_onoff"), "heater must not be turned off before the end of the control period");
            MakeDecisions(time: 30);
            Assert.AreEqual(0.0, Field("heater_onoff"), "heater must be turned off on the next control period");
        }

        [TestMethod]
        //Important: at the time of writing, even though the oven target can be changed by other plugins, only the oven command currently applies it within the same control period
        public void StateReflectLatestChangesDoneWithTheOvenCommand()
        {
            MakeDecisions("oven 200");
            Assert.AreEqual(200, Field("ovenarea1_target"));
            Assert.AreEqual(HeatingController.HeaterDecision.States.InControlPeriod, (HeatingController.HeaterDecision.States)Field("heater_state"));
            Assert.AreEqual(testContext.InitialTime.AddSeconds(30), Field("heater_nextcontrolperiod").ToVectorDate());
            Assert.AreEqual(testContext.InitialTime.AddSeconds(30), Field("heater_controlperiodtimeoff").ToVectorDate());
            MakeDecisions("oven 94", 1);
            Assert.AreEqual(94, Field("ovenarea1_target"));
            Assert.AreEqual(HeatingController.HeaterDecision.States.InControlPeriod, (HeatingController.HeaterDecision.States)Field("heater_state"));
            Assert.AreEqual(testContext.InitialTime.AddSeconds(30), Field("heater_nextcontrolperiod").ToVectorDate());
            Assert.AreEqual(testContext.InitialTime.AddSeconds(10), Field("heater_controlperiodtimeoff").ToVectorDate(), "time off not as expected after a temp change (10 seconds after the cycle started)");
            Field("temp") = 150; //setting temp higher than last target to ensure we show the heater stays on regardless
            MakeDecisions("heater heater on", time: 31);
            Assert.AreEqual(94, Field("ovenarea1_target"), "target unexpectedly changed when specifying manual mode");
            Assert.AreEqual(HeatingController.HeaterDecision.States.ManualOn, (HeatingController.HeaterDecision.States)Field("heater_state"), "manual on not reflected on state");
            Assert.AreEqual(1.0, Field("heater_onoff"), "heater not on on manual mode");
            MakeDecisions("heater heater off", time: 32);
            Assert.AreEqual(HeatingController.HeaterDecision.States.InControlPeriod, (HeatingController.HeaterDecision.States)Field("heater_state"), "manual off not reflected on state");
            Assert.AreEqual(0.0, Field("heater_onoff"), "heater not off after shutting off manual mode");
        }

        [TestMethod]
        //Important: at the time of writing, even though the oven target can be changed by other plugins, only the oven command currently applies it within the same control period
        public void WhenTargetIsChangedActuatesImmediately()
        {
            MakeDecisions("oven 40");
            MakeDecisions("oven 100", 1);
            Assert.AreEqual(1.0, Field("heater_onoff"));
            MakeDecisions("oven 40", 2);
            Assert.AreEqual(0.0, Field("heater_onoff"));
        }

        [TestMethod]
        public void CanRunWithoutOven()
        {
            ReplaceConfig(NewConfig.WithoutOven());
            MakeDecisions();
            Assert.AreEqual(0.0, Field("heater_onoff"));
            MakeDecisions("heater heater on", 5);
            Assert.AreEqual(1.0, Field("heater_onoff"));
            MakeDecisions("heater heater off", 10);
            Assert.AreEqual(0.0, Field("heater_onoff"), "should be off after turning off again");
        }

        [TestMethod]
        public void CanEmergencyShutdownWithoutOven()
        {
            ReplaceConfig(NewConfig.WithoutOven());
            MakeDecisions("heater heater on", 5);
            Assert.AreEqual(1.0, Field("heater_onoff"));
            MakeDecisions("emergencyshutdown", 10);
            Assert.AreEqual(0.0, Field("heater_onoff"));
        }

        [TestMethod]
        public void PControlFieldsAreNotAddedByDefault()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Field("ovenarea1_pgain"));
            Assert.Throws<ArgumentOutOfRangeException>(() => Field("ovenarea1_controlperiod"));
            Assert.Throws<ArgumentOutOfRangeException>(() => Field("ovenarea1_maxoutput"));
        }

        [TestMethod]
        public void PControlUsesConfigValuesByDefault()
        {
            ReplaceConfig(NewConfig.WithProportionalGain(2), extraLines: "OvenProportionalControlUpdates;5;00:02:00;100");
            CheckDecisionsBehaviorMatchesAProportionalGainOf2();
        }

        [TestMethod]
        public void PControlUsesMaxConfigurationForCommandValuesAboveAllowedRange()
        {
            ReplaceConfig(
                NewConfig.WithProportionalGain(1).WithMaxOutput(0.5).WithControlPeriodSeconds(15), extraLines: "OvenProportionalControlUpdates;2;00:00:30;100");
            CheckDecisionsBehaviorMatchesAProportionalGainOf2(["ovenarea 1 pgain 10", "ovenarea 1 maxoutput 150", "ovenarea 1 controlperiodseconds 120"]);
        }

        [DataRow(-1, 2, false)]
        [DataRow(-100, 2, false)]
        [DataRow(100, -1, false)]
        [DataRow(100, -100, false)]
        [DataRow(-1, 2, true)]
        [DataRow(-100, 2, true)]
        [DataRow(100, -1, true)]
        [DataRow(100, -100, true)]
        [TestMethod]//note we don't include 0 above, as that is the default vector value which is interpreted as the field not being set
        public void PControlDoesNotTurnOnWithMaxOutputAndOrPGainValuesBelow0(int maxoutput, int pgain, bool useVector)
        {
            ReplaceConfig(extraLines: "OvenProportionalControlUpdates;2;00:00:30;100");
            MakeDecisions(["ovenarea 1 maxoutput -1", "oven 50"]);
            if (useVector)
            {
                Field("ovenarea1_maxoutput") = maxoutput;
                Field("ovenarea1_pgain") = pgain;
                MakeDecisions("oven 50");
            }
            else
                MakeDecisions([$"ovenarea 0 maxoutput {maxoutput}", $"ovenarea 1 pgain {pgain}", "oven 50"]);

            Assert.AreEqual(0.0, Field("heater_onoff"), "should be off");
            Field("temp") = 10;
            MakeDecisions(time: 30);
            Assert.AreEqual(0.0, Field("heater_onoff"), "should still be off regardless of having a large temp difference");
            MakeDecisions(time: 60);
            Assert.AreEqual(0.0, Field("heater_onoff"), "should still be off after 60 seconds");
            Field("temp") = 90;
            MakeDecisions(time: 90);
            Assert.AreEqual(0.0, Field("heater_onoff"), "this would only turn on if the pgain is unexpectedly negative");
        }

        [DataRow(-1, false)]
        [DataRow(-1000, false)]
        [DataRow(0.1, false)]
        [DataRow(-1, true)]
        [DataRow(-1000, true)]
        [DataRow(0.1, true)]
        [TestMethod] //note we don't include 0 above, as that is the default vector value which is interpreted as the field not being set
        public void PControlReactsImmediatelyWhenControlPeriodIsBelowOrEqual100Ms(double controlperiodseconds, bool useVector)
        {
            ReplaceConfig(NewConfig.WithProportionalGain(2), extraLines: "OvenProportionalControlUpdates;2;00:00:30;100");
            if (useVector)
            {
                Field("ovenarea1_controlperiodseconds") = controlperiodseconds;
                MakeDecisions("oven 50");
            }
            else
                MakeDecisions([Invariant($"ovenarea 1 controlperiodseconds {controlperiodseconds}"), "oven 50"]);
            Assert.AreEqual(1.0, Field("heater_onoff"), "should be on as we are below the target");
            Field("temp") = 49;
            MakeDecisions(time: 0.1);
            Assert.AreEqual(1.0, Field("heater_onoff"), "should still be on 1C below target");
            Field("temp") = 51;
            MakeDecisions(time: 0.2);
            Assert.AreEqual(0.0, Field("heater_onoff"), "should turn off immediately on the cycle ");
            Field("temp") = 49;
            MakeDecisions(time: 0.3);
            Assert.AreEqual(1.0, Field("heater_onoff"), "should still turn on imediately");
            MakeDecisions(time: 0.4);
            Assert.AreEqual(1.0, Field("heater_onoff"), "it should decide to keep the heater on");
            MakeDecisions(time: 29);
            Assert.AreEqual(1.0, Field("heater_onoff"), "it should still decide to keep the heater on");
            Field("temp") = 51;
            MakeDecisions(time: 29.1);
            Assert.AreEqual(0.0, Field("heater_onoff"), "should still turn off immediately");
        }

        [TestMethod]
        public void PControlUsesMaxConfigurationForVectorValuesAboveAllowedRange()
        {
            ReplaceConfig(
                NewConfig.WithProportionalGain(1).WithMaxOutput(0.5).WithControlPeriodSeconds(15), 
                extraLines: "OvenProportionalControlUpdates;2;00:00:30;100");
            Field("ovenarea1_pgain") = 10;
            Field("ovenarea1_maxoutput") = 1.5;
            Field("ovenarea1_controlperiodseconds") = 120;
            CheckDecisionsBehaviorMatchesAProportionalGainOf2();
        }

        [TestMethod]
        public void PControlUsesUpdatedGainField()
        {
            ReplaceConfig(NewConfig.WithProportionalGain(3), extraLines: "OvenProportionalControlUpdates;5;00:02:00;100");
            CheckDecisionsBehaviorMatchesAProportionalGainOf2("ovenarea 1 pgain 2");
        }

        [TestMethod]
        public void PControlUsesUpdatedMaxOutputField()
        {
            ReplaceConfig(NewConfig.WithProportionalGain(2).WithMaxOutput(0.2), extraLines: "OvenProportionalControlUpdates;5;00:02:00;100");
            CheckDecisionsBehaviorMatchesAProportionalGainOf2("ovenarea 1 maxoutput 100");
        }

        [TestMethod]
        public void PControlUsesUpdatedControlPeriod()
        {
            ReplaceConfig(NewConfig.WithProportionalGain(2).WithControlPeriodSeconds(10), extraLines: ("OvenProportionalControlUpdates;5;00:02:00;100"));
            CheckDecisionsBehaviorMatchesAProportionalGainOf2("ovenarea 1 controlperiodseconds 30");
        }

        private class HeaterDecisionConfigBuilder
        {
            private double _proportionalGain = 0.2d;
            private double _maxOutput = 1d;
            private bool _ovenDisabled = false;
            private double _controlPeriodSeconds = 30;

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

            public HeaterDecisionConfigBuilder WithControlPeriodSeconds(double controlPeriodSeconds)
            {
                _controlPeriodSeconds = controlPeriodSeconds;
                return this;
            }

            public HeaterDecisionConfigBuilder WithoutOven()
            {
                _ovenDisabled = true;
                return this;
            }

            public string Build() => _ovenDisabled
? @"Map;fakeac;ac01
Heater;heater;ac01;1"
: Invariant(@$"Map;fakeac;ac01
Heater;heater;ac01;1;800
Oven;1;heater;temp;{_proportionalGain};{TimeSpan.FromSeconds(_controlPeriodSeconds)};{_maxOutput * 100}
Map;fakeserial2;temperature
TypeK;temp;temperature;1");
        }
    }
}