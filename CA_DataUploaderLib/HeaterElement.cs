using CA.LoopControlPluginBase;
using CA_DataUploaderLib.Extensions;
using CA_DataUploaderLib.IOconf;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace CA_DataUploaderLib
{
    public class HeaterElement
    {
        private readonly HeaterElementState State = new ();
        private readonly Config _config;
        public bool IsActive => State.OvenOn || State.ManualOn;
        private readonly Stopwatch timeSinceLastNegativeValuesWarning = new();
        private readonly Stopwatch timeSinceLastMissingTemperatureWarning = new();
        private readonly Stopwatch timeSinceLastMissingTemperatureBoardWarning = new();

        public HeaterElement(IOconfHeater heater, IOconfOven oven) : this(ToConfig(heater, oven))
        {
        }

        public HeaterElement(Config config)
        {
            _config = config;
        }

        public IEnumerable<SensorSample> MakeNextActionDecision(NewVectorReceivedArgs vector) => GetUpdatedDecisionState(vector).ToVectorSamples(_config.Name);
        public HeaterElementState GetUpdatedDecisionState(NewVectorReceivedArgs vector)
        {
            var vectorTime = vector.GetVectorTime();
            var (hasValidTemperature, temp) = GetOvenTemperatureFromVector(vector);
            // Careful consideration must be taken if changing the order of the below statements.
            State.IsOn = IsOn(hasValidTemperature, temp, vectorTime);
            return State;
        }

        public void SetTargetTemperature(int value) => State.SetTarget(Math.Min(value, _config.MaxTemperature), _config.ControlPeriod);
        public void SetTargetTemperature(IEnumerable<(int area, int temperature)> values)
        {
            if (_config.Area == -1) return; // temperature control is not enabled for the heater (no oven or missing temperature hub)

            foreach (var (area, temperature) in values)
                if (_config.Area == area)
                    SetTargetTemperature(temperature);
        }

        public IEnumerable<VectorDescriptionItem> GetStateVectorDescriptionItems() => HeaterElementState.GetVectorDescriptionItems(Name());
        private bool IsOn(bool hasValidTemperature, double temperature, DateTime vectorTime)
        { 
            if (State.ManualOn) return true;
            if (!State.OvenOn) return false;
            var isNextControlPeriod = (vectorTime - State.CurrentControlPeriodStart) >= _config.ControlPeriod;
            if (!isNextControlPeriod && State.CurrentControlPeriodTimeOff > vectorTime) return true;
            if (!isNextControlPeriod) return false;
            if (!hasValidTemperature) return false; //postpone the start of the control period until we get valid temperatures

            var secondsOn = GetProportionalControlSecondsOn(temperature);
            //SetTargetTemperature can trigger a new control period with a lower target,
            //due to this we explicitely issue an off action here as a result of the p control
            //i.e. we can't assume the last control period is always shut off when reaching the end of the control period
            State.SetPControlDecision(vectorTime, secondsOn);
            return secondsOn != 0;

            double GetProportionalControlSecondsOn(double currentTemperature)
            {
                var tempDifference = State.Target - currentTemperature;
                var secondsOn = tempDifference * _config.ProportionalGain;
                return secondsOn < 0.1d 
                    ? 0d //we can't turn on less than the decision cycle duration
                    : Math.Min(secondsOn, _config.MaxOutputPercentage * _config.ControlPeriod.TotalSeconds); //we act max for this control period so we can re-evaluate where we are for next actuation
            }
        }

        public void ResumeState(NewVectorReceivedArgs args) => State.ResumeFromVectorSamples(Name(), args);
        public void SetManualMode(bool turnOn) => State.ManualOn = turnOn;
        private (bool hasValidTemperature, double temp) GetOvenTemperatureFromVector(NewVectorReceivedArgs vector) 
        {
            var sensor = _config.OvenSensor;
            if (sensor == null) return (false, double.MaxValue);
            if (!TemperatureBoardsAreConnected(vector, _config.TemperatureBoardStateSensorNames))
                return (false, double.MaxValue); 
            if (!vector.TryGetValue(sensor, out var val))
                WarnMissingSensor(sensor);
            else if (val <= 10000 && val > 0) // we only use valid positive values. Note some temperature hubs alternate between 10k+ and 0 for errors.
                return (true, val);
            else if (val < 0)
                WarnNegativeTemperatures();
            return (false, double.MaxValue);
        }

        private bool TemperatureBoardsAreConnected(NewVectorReceivedArgs vector, IReadOnlyCollection<string> boardStateSensorNames)
        {
            foreach (var board in boardStateSensorNames)
            {
                if (!vector.TryGetValue(board, out var state))
                {//seeing this warning must likely would mean there is a bug, as config validations and overall design is supposed to ensure we always have the board state in the vector.
                    LowFrequencyWarning(timeSinceLastMissingTemperatureBoardWarning, $"detected missing temperature board {board} in vector for heater {Name()}.");
                    return false;
                }

                if (state != (int)BaseSensorBox.ConnectionState.ReceivingValues)
                    return false;
            }

            return true;
        }

        private void WarnNegativeTemperatures() =>
            LowFrequencyWarning(
                timeSinceLastNegativeValuesWarning,
                $"detected negative values in sensors for heater {this.Name()}. Confirm thermocouples cables are not inverted");
        private void WarnMissingSensor(string sensorName) =>
            LowFrequencyWarning(
                timeSinceLastMissingTemperatureWarning,
                $"detected missing temperature sensor {sensorName} for heater {Name()}. Confirm the name is listed exactly as is in the plot.");
        private static void LowFrequencyWarning(Stopwatch watch, string message)
        {
            if (watch.IsRunning && watch.Elapsed.Hours < 1) return;
            CALog.LogErrorAndConsoleLn(LogID.A,message);
            watch.Restart();
        }
        public string Name() => _config.Name;
        private static Config ToConfig(IOconfHeater heater, IOconfOven oven)
        {
            if (oven == null)
            {
                CALog.LogInfoAndConsoleLn(LogID.A, $"Warn: no oven configured for heater {heater.Name}");
                return new ()
                {
                    MaxTemperature = heater.MaxTemperature,
                    Name = heater.Name
                };
            }
            
            return new ()
            {
                MaxTemperature = heater.MaxTemperature,
                Name = heater.Name,
                Area = oven.OvenArea,
                OvenSensor = oven.TemperatureSensorName,
                ProportionalGain = oven.ProportionalGain,
                ControlPeriod = oven.ControlPeriod,
                MaxOutputPercentage = oven.MaxOutputPercentage,
                TemperatureBoardStateSensorNames = oven.BoardStateSensorNames
            };
        }

        public class Config
        {
            public int Area { get; init; } = -1; // -1 when not set
            public string OvenSensor { get; init; } // sensor inside the oven somewhere.
            public int MaxTemperature { get; init; }
            public string Name { get; init; }
            public IReadOnlyCollection<string> TemperatureBoardStateSensorNames { get; init; }
            public double ProportionalGain { get; init; }
            public TimeSpan ControlPeriod { get; init; }
            public double MaxOutputPercentage { get; init; }
        }

        public class HeaterElementState
        {
            public bool IsOn { get; internal set; }
            public int Target { get; private set; }
            public DateTime CurrentControlPeriodStart { get; private set; }
            public double CurrentControlPeriodSecondsOn { get; private set; }
            public DateTime CurrentControlPeriodTimeOff { get; private set; }
            public bool ManualOn { get; internal set; }
            public bool OvenOn => Target > 0;

            internal void SetTarget(int temperature, TimeSpan controlPeriod)
            {
                Target = temperature;
                if (!OvenOn)
                    ManualOn = false; // ensures oven off also turns off any manual heater.
                if (CurrentControlPeriodStart != default)
                    CurrentControlPeriodStart -= controlPeriod; //ensure we'll start a new control period on the next decision, avoiding slow response to commands.
            }

            internal IEnumerable<SensorSample> ToVectorSamples(string name)
            {
                yield return new SensorSample(name + "_onoff", IsOn ? 1.0 : 0.0); //TODO: hotvalves and valves need the same treatment!
                yield return new SensorSample(name + "_target", Target);
                yield return new SensorSample(name + "_pcontrolstart", CurrentControlPeriodStart.ToVectorDouble());
                yield return new SensorSample(name + "_pcontrolseconds", CurrentControlPeriodSecondsOn);
                yield return new SensorSample(name + "_manualon", ManualOn ? 1.0 : 0.0);
            }

            internal static IEnumerable<VectorDescriptionItem> GetVectorDescriptionItems(string name)
            {
                yield return new VectorDescriptionItem("double", name + "_onoff", DataTypeEnum.Output);
                yield return new VectorDescriptionItem("double", name + "_target", DataTypeEnum.State);
                yield return new VectorDescriptionItem("double", name + "_pcontrolstart", DataTypeEnum.State);
                yield return new VectorDescriptionItem("double", name + "_pcontrolseconds", DataTypeEnum.State);
                yield return new VectorDescriptionItem("double", name + "_manualon", DataTypeEnum.State);
            }
            internal void ResumeFromVectorSamples(string name, NewVectorReceivedArgs args)
            {
                IsOn = args[name + "_onoff"] == 1.0;
                Target = (int)args[name + "_target"];
                CurrentControlPeriodStart = args[name + "_pcontrolstart"].ToVectorDate();
                CurrentControlPeriodSecondsOn = args[name + "_pcontrolseconds"];
                CurrentControlPeriodTimeOff = CurrentControlPeriodStart.AddSeconds(CurrentControlPeriodSecondsOn);
                ManualOn = args[name + "_manualon"] == 1.0;
            }
            internal void SetPControlDecision(DateTime vectorTime, double secondsOn)
            {
                CurrentControlPeriodStart = vectorTime;
                CurrentControlPeriodSecondsOn = Math.Round(secondsOn, 4);
                CurrentControlPeriodTimeOff = vectorTime.AddSeconds(CurrentControlPeriodSecondsOn);
            }
        }
    }
}
