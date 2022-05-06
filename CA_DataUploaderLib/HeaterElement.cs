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
        private int OvenTargetTemperature;
        private readonly Config _config;
        private DateTime globalTimeOff;
        private bool ManualTurnOn;
        public bool IsActive { get { return OvenTargetTemperature > 0;  } }
        private readonly Stopwatch timeSinceLastNegativeValuesWarning = new();
        private readonly Stopwatch timeSinceLastMissingTemperatureWarning = new();
        private readonly Stopwatch timeSinceLastMissingTemperatureBoardWarning = new();
        private SwitchboardAction _lastAction = new(false, DateTime.UtcNow);
        private DateTime LastAutoOn;

        public HeaterElement(IOconfHeater heater, IOconfOven oven) : this(ToConfig(heater, oven))
        {
        }

        public HeaterElement(Config config)
        {
            _config = config;
        }

        public SwitchboardAction MakeNextActionDecision(NewVectorReceivedArgs vector)
        {
            if (!TryGetSwitchboardInputsFromVector(vector, out var current)) 
                return _lastAction; // not connected, we skip this heater and act again when the connection is re-established
            var (hasValidTemperature, temp) = GetOvenTemperatureFromVector(vector);
            // Careful consideration must be taken if changing the order of the below statements.
            // Note that even though we received indication the board is connected above, 
            // if the connection is lost after we return the action, the control program can still fail to act on the heater. 
            // When it happens, the MustResend* methods will resend the expected action after 5 seconds.
            var vectorTime = vector.GetVectorTime();
            var action =
                ManualTurnOn && !_lastAction.IsOn ? new SwitchboardAction(true, vectorTime.AddSeconds(10)) :
                MustTurnOff(vectorTime) ? new SwitchboardAction(false, vectorTime) :
                CanTurnOn(hasValidTemperature, temp, vectorTime) ? new SwitchboardAction(true, vectorTime.AddSeconds(10)) :
                // keep off: retrigger if current is detected
                MustResendOffCommand(temp, current, vectorTime) ? new SwitchboardAction(false, vectorTime) :
                // keep on: re-trigger early to avoid switching
                _lastAction.IsOn && _lastAction.GetRemainingOnSeconds(vectorTime) < 2 ? new SwitchboardAction(true, vectorTime.AddSeconds(10)) : 
                _lastAction;
            return _lastAction = action;
        }

        public void SetTargetTemperature(int value)
        { 
            OvenTargetTemperature = Math.Min(value, _config.MaxTemperature);
            if (value <= 0)
                SetManualMode(false); // this ensures that executing oven off also turns off any manual heater.
        }
        public void SetTargetTemperature(IEnumerable<(int area, int temperature)> values)
        {
            if (_config.Area == -1) return; // temperature control is not enabled for the heater (no oven or missing temperature hub)

            foreach (var (area, temperature) in values)
                if (_config.Area == area)
                    SetTargetTemperature(temperature);
        }

        private bool CanTurnOn(bool hasValidTemperature, double temperature, DateTime vectorTime)
        {
            if (_lastAction.IsOn) return false; // already on
            if (OvenTargetTemperature <= 0) return false; // oven's command is off, skip any extra checks
            if (!hasValidTemperature) return false; // no valid oven sensors

            globalTimeOff = GetProportionalControlTimeOff(temperature, vectorTime);
            return globalTimeOff != vectorTime;
        }

        private DateTime GetProportionalControlTimeOff(double currentTemperature, DateTime vectorTime)
        {
            if ((vectorTime - LastAutoOn) < _config.ControlPeriod)
                return vectorTime; //less than the control period since we last turned it on

            var gain = _config.ProportionalGain;
            var tempDifference = OvenTargetTemperature - currentTemperature;
            var secondsOn = tempDifference * gain;
            if (secondsOn < 0.1d) //we can't turn off less than the decision cycle duration (switchboards take at least 1 second so we explicitely shut off on the next cycle).
                return vectorTime;

            secondsOn = Math.Min(secondsOn, _config.MaxOutputPercentage * _config.ControlPeriod.TotalSeconds); //we act max for this control period so we can re-evaluate where we are for next actuation
            LastAutoOn = vectorTime;
            return vectorTime.AddSeconds(secondsOn);
        }

        private bool MustTurnOff(DateTime vectorTime)
        {
            if (!_lastAction.IsOn) return false; // already off
            if (ManualTurnOn) return false; // heater is on in manual mode, avoid turning off
            if (OvenTargetTemperature <= 0)
                return true;

            return vectorTime >= globalTimeOff;
        }

        public void SetManualMode(bool turnOn) => ManualTurnOn = turnOn;
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

        // resends the off command every 5 seconds as long as there is current.
        private bool MustResendOffCommand(double temp, double current, DateTime vectorTime) 
        { 
            if (_lastAction.IsOn || vectorTime < _lastAction.TimeToTurnOff.AddSeconds(5) || !CurrentIsOn(current)) return false;
            LogRepeatCommand("off", temp, current);
            return true;
        }
        private bool CurrentIsOn(double current) => current > _config.CurrentSensingNoiseTreshold;
        public string Name() => _config.Name;
        private void LogRepeatCommand(string command, double  temp, double current) => 
            CALog.LogData(LogID.A, $"{command}.={Name()}-{temp:N0}, v#={current}{Environment.NewLine}");

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
    }
}
