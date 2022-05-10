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

        public IHeaterElementState MakeNextActionDecision(NewVectorReceivedArgs vector)
        {
            if (!TryGetSwitchboardInputsFromVector(vector, out var current)) 
                return State; // not connected, we skip this heater and act again when the connection is re-established
            var (hasValidTemperature, temp) = GetOvenTemperatureFromVector(vector);
            // Careful consideration must be taken if changing the order of the below statements.
            var vectorTime = vector.GetVectorTime();
            State.Action = CanTurnOn(hasValidTemperature, temp, vectorTime) 
                ?? MustTurnOff(current, vectorTime) 
                ?? State.Action;
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

        /// <returns>an on action for up to 10 seconds (max time to leave on if we lose switchboard comms) or <c>null</c> when there is no new on action</returns>
        private SwitchboardAction CanTurnOn(bool hasValidTemperature, double temperature, DateTime vectorTime)
        { 
            if (State.NeedToExtendManualOnAction(vectorTime)) return new (true, vectorTime.AddSeconds(10));
            if (State.ManualOn) return null;
            if (!State.OvenOn) return null;
            var isNextControlPeriod = (vectorTime - State.CurrentControlPeriodStart) >= _config.ControlPeriod;
            if (!isNextControlPeriod && State.NeedToExtendCurrentControlPeriodAction(vectorTime)) return new (true, UpTo10Seconds(State.CurrentControlPeriodTimeOff));
            if (!isNextControlPeriod || !hasValidTemperature) return null;
            State.CurrentControlPeriodStart = vectorTime; //note we postpone the start of the control period above if we don't have valid temperatures

            var timeOff = GetProportionalControlTimeOff(temperature, vectorTime);
            State.CurrentControlPeriodTimeOff = timeOff;
            //SetTargetTemperature can trigger a new control period with a lower target,
            //due to this we explicitely issue off actions as a result of the p control
            //i.e. we can't assume the last control period is always shut off when reaching the end of the control period
            return new(timeOff != vectorTime, UpTo10Seconds(State.CurrentControlPeriodTimeOff));


            DateTime UpTo10Seconds(DateTime timeOff) => Min(timeOff, vectorTime.AddSeconds(10));
            static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;
            DateTime GetProportionalControlTimeOff(double currentTemperature, DateTime vectorTime)
            {
                var gain = _config.ProportionalGain;
                var tempDifference = State.Target - currentTemperature;
                var secondsOn = tempDifference * gain;
                if (secondsOn < 0.1d) //we can't turn on less than the decision cycle duration
                    return vectorTime;

                secondsOn = Math.Min(secondsOn, _config.MaxOutputPercentage * _config.ControlPeriod.TotalSeconds); //we act max for this control period so we can re-evaluate where we are for next actuation
                return vectorTime.AddSeconds(secondsOn);
            }
        }

        public void ResumeState(NewVectorReceivedArgs args)
        {
            State.ResumeFromVectorSamples(Name(), args);
        }

        /// <returns>the off action or <c>null</c> when there is no new off action</returns>
        private SwitchboardAction MustTurnOff(double current, DateTime vectorTime)
        {
            if (!State.Action.IsOn && vectorTime >= State.Action.TimeToTurnOff.AddSeconds(5) && current > _config.CurrentSensingNoiseTreshold) 
                return new(false, vectorTime);//current detected with heater off, resending off command every 5 seconds
            if (!State.Action.IsOn) return null;
            if (State.ManualOn) return null;
            if (!State.OvenOn || vectorTime >= State.CurrentControlPeriodTimeOff)
                return new (false, vectorTime);
            return null;
        }

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

        private bool TryGetSwitchboardInputsFromVector(
            NewVectorReceivedArgs vector, out double current)
        {
            if (!vector.TryGetValue(_config.SwitchBoardStateSensorName, out var state))
                throw new InvalidOperationException($"missing heater's board connection state: {_config.SwitchBoardStateSensorName}");
            if (state != (int)BaseSensorBox.ConnectionState.ReceivingValues)
            {
                current = 0;
                return false;
            }

            if (!vector.TryGetValue(_config.CurrentSensorName, out current))
                throw new InvalidOperationException($"missing heater current: {_config.CurrentSensorName}");
            return true;
        }

        private static Config ToConfig(IOconfHeater heater, IOconfOven oven)
        {
            if (oven == null)
            {
                CALog.LogInfoAndConsoleLn(LogID.A, $"Warn: no oven configured for heater {heater.Name}");
                return new ()
                {
                    MaxTemperature = heater.MaxTemperature,
                    CurrentSensingNoiseTreshold = heater.CurrentSensingNoiseTreshold,
                    Name = heater.Name,
                    SwitchBoardStateSensorName = heater.BoardStateSensorName,
                    CurrentSensorName = heater.CurrentSensorName,
                };
            }
            
            return new ()
            {
                MaxTemperature = heater.MaxTemperature,
                CurrentSensingNoiseTreshold = heater.CurrentSensingNoiseTreshold,
                Name = heater.Name,
                SwitchBoardStateSensorName = heater.BoardStateSensorName,
                CurrentSensorName = heater.CurrentSensorName,
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
            public double CurrentSensingNoiseTreshold { get; init; }
            public string Name { get; init; }
            public string SwitchBoardStateSensorName { get; init; }
            public string CurrentSensorName { get; init; }
            public IReadOnlyCollection<string> TemperatureBoardStateSensorNames { get; init; }
            public double ProportionalGain { get; init; }
            public TimeSpan ControlPeriod { get; init; }
            public double MaxOutputPercentage { get; init; }
        }

        private class HeaterElementState : IHeaterElementState
        {
            public SwitchboardAction Action { get; set; } = new(false, default);
            public int Target { get; private set; }
            public DateTime CurrentControlPeriodStart { get; set; }
            public DateTime CurrentControlPeriodTimeOff { get; set; }
            public bool ManualOn { get; set; }
            public bool OvenOn => Target > 0;

            public void SetTarget(int temperature, TimeSpan controlPeriod)
            {
                Target = temperature;
                if (!OvenOn)
                    ManualOn = false; // ensures oven off also turns off any manual heater.
                if (CurrentControlPeriodStart != default)
                    CurrentControlPeriodStart -= controlPeriod; //ensure we'll start a new control period on the next decision, avoiding slow response to commands.
            }

            public IEnumerable<SensorSample> ToVectorSamples(string name, DateTime vectorTime)
            {
                foreach (var sample in Action.ToVectorSamples(name, vectorTime))
                    yield return sample;
                yield return new SensorSample(name + "_target", Target);
                yield return new SensorSample(name + "_pcontrolstart", CurrentControlPeriodStart.ToVectorDouble());
                yield return new SensorSample(name + "_pcontroltimeoff", CurrentControlPeriodTimeOff.ToVectorDouble()); //TODO: changing this to seconds would give nice info!
                yield return new SensorSample(name + "_manualon", ManualOn ? 1.0 : 0.0);
            }

            public static IEnumerable<VectorDescriptionItem> GetVectorDescriptionItems(string name)
            {
                foreach (var item in SwitchboardAction.GetVectorDescriptionItems(name))
                    yield return item;
                yield return new VectorDescriptionItem("double", name + "_target", DataTypeEnum.State);
                yield return new VectorDescriptionItem("double", name + "_pcontrolstart", DataTypeEnum.State);
                yield return new VectorDescriptionItem("double", name + "_pcontroltimeoff", DataTypeEnum.State);
                yield return new VectorDescriptionItem("double", name + "_manualon", DataTypeEnum.State);
            }

            public bool NeedToExtendCurrentControlPeriodAction(DateTime vectorTime) => CurrentControlPeriodTimeOff > Action.TimeToTurnOff && Action.GetRemainingOnSeconds(vectorTime) < 2;
            public bool NeedToExtendManualOnAction(DateTime vectorTime) => ManualOn && Action.GetRemainingOnSeconds(vectorTime) < 2;
            public void ResumeFromVectorSamples(string name, NewVectorReceivedArgs args)
            {
                Action = SwitchboardAction.FromVectorSamples(args, name);
                Target = (int)args[name + "_target"];
                CurrentControlPeriodStart = args[name + "_pcontrolstart"].ToVectorDate();
                CurrentControlPeriodTimeOff = args[name + "_pcontroltimeoff"].ToVectorDate();
                ManualOn = args[name + "_manualon"] == 1.0;
            }
        }
        public interface IHeaterElementState
        {
            public SwitchboardAction Action { get; }
            public int Target { get; }
            public DateTime CurrentControlPeriodStart { get; }
            public DateTime CurrentControlPeriodTimeOff { get; }
            public bool ManualOn { get; }
            public bool IsOn => Action.IsOn;
            public DateTime TimeToTurnOff => Action.TimeToTurnOff;

            IEnumerable<SensorSample> ToVectorSamples(string name, DateTime dateTime);
        }
    }
}
