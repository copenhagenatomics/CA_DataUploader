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
        private Config _config;
        private DateTime LastOff = DateTime.MinValue; // assume there is no previous off
        private DateTime LastAutoOff = DateTime.MinValue; // assume there is no previous off
        private DateTime invalidValuesStartedVectorTime = default;
        private double onTemperature = 10000;
        public bool IsOn;
        private bool ManualTurnOn;
        private bool PendingManualModeExecution;
        public bool IsActive { get { return OvenTargetTemperature > 0;  } }
        private Stopwatch timeSinceLastNegativeValuesWarning = new Stopwatch();
        private Stopwatch timeSinceLastMissingTemperatureWarning = new Stopwatch();
        private SwitchboardAction _lastAction = new SwitchboardAction(false, DateTime.UtcNow);

        public HeaterElement(IOconfHeater heater, IOconfOven oven) : this(ToConfig(heater, oven))
        {
        }

        public HeaterElement(Config config)
        {
            _config = config;
        }

        public SwitchboardAction MakeNextActionDecision(NewVectorReceivedArgs vector)
        {
            if (!TryGetSwitchboardInputsFromVector(vector, out var current, out var switchboardOnOffState)) 
                return _lastAction; // not connected, we skip this heater and act again when the connection is re-established
            var (hasValidTemperature, temp) = GetOvenTemperatureFromVector(vector);
            // Careful consideration must be taken if changing the order of the below statements.
            // Note that even though we received indication the board is connected above, 
            // if the connection is lost after we return the action, the control program can still fail to act on the heater. 
            // When it happens, the MustResend* methods will resend the expected action after 5 seconds.
            var vectorTime = vector.GetVectorTime();
            var (executeManualOff, executeManualOn) = MustExecuteManualMode(vectorTime);
            var action =
                executeManualOn ? new SwitchboardAction(true, vectorTime.AddSeconds(10)) :
                MustTurnOff(hasValidTemperature, temp, vectorTime) ? new SwitchboardAction(false, vectorTime) :
                CanTurnOn(hasValidTemperature, temp, vectorTime) ? new SwitchboardAction(true, vectorTime.AddSeconds(10)) : 
                // manual off re-enables temp control, so we only turn off if CanTurnOn above didn't decide to turn on
                executeManualOff ? new SwitchboardAction(false, vectorTime) :
                // keep off: retrigger if current is detected
                MustResendOffCommand(temp, current, switchboardOnOffState, vectorTime) ? new SwitchboardAction(false, vectorTime) :
                // keep on: re-trigger early to avoid switching
                _lastAction.IsOn && _lastAction.GetRemainingOnSeconds(vectorTime) < 2 ? new SwitchboardAction(true, vectorTime.AddSeconds(10)) : 
                _lastAction;
            return _lastAction = action;
        }

        public (bool manualOff, bool manualOn) MustExecuteManualMode(DateTime vectorTime)
        {
            if (!PendingManualModeExecution) return (false, false);
            PendingManualModeExecution = false;
            if (!ManualTurnOn)
                SetOffProperties(vectorTime);
            else
                SetOnProperties(vectorTime);
            return (!ManualTurnOn, ManualTurnOn);
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
            if (IsOn) return false; // already on
            if (OvenTargetTemperature <= 0) return false; // oven's command is off, skip any extra checks
            if (!hasValidTemperature) return false; // no valid oven sensors

            if (LastAutoOff > vectorTime.AddSeconds(-10))
                return false;  // less than 10 seconds since we last turned it off

            if (temperature >= OvenTargetTemperature)
                return false; // already at target temperature. 

            onTemperature = temperature;
            return SetOnProperties(vectorTime);
        }

        private bool MustTurnOff(bool hasValidTemperature, double temperature, DateTime vectorTime)
        {
            if (!IsOn) return false; // already off
            if (ManualTurnOn) return false; // heater is on in manual avoid, avoid turning off
            if (OvenTargetTemperature <= 0)
                return SetOffProperties(vectorTime); // turn off: oven's command is off, skip any extra checks (note its not considered an auto off so new oven commands are not hit by the 10 seconds limit)
            var timeoutResult = CheckInvalidValuesTimeout(hasValidTemperature, 2000, vectorTime);
            if (timeoutResult.HasValue && timeoutResult.Value)
                return SetAutoOffProperties(vectorTime); // turn off: 2 seconds without valid temperatures
            else if (timeoutResult.HasValue)
                return false; // no valid temperatures, waiting up to 2 seconds before turn off

            if (onTemperature < 10000 && temperature > onTemperature + 20)
                return SetAutoOffProperties(vectorTime); // turn off: already 20C higher than the last time we turned on

            if (temperature > OvenTargetTemperature)
                return SetAutoOffProperties(vectorTime); //turn off: already at target temperature

            return false;
        }

        // returns true to simplify MustTurnOff
        private bool SetAutoOffProperties(DateTime vectorTime)
        {
            LastAutoOff = vectorTime;
            return SetOffProperties(vectorTime);
        }

        private bool SetOffProperties(DateTime vectorTime)
        {
            IsOn = false;
            LastOff = vectorTime;
            return true;
        }

        private bool SetOnProperties(DateTime vectorTime)
        {
            IsOn = true;
            return true;
        }

        public void SetManualMode(bool turnOn)
        { 
            ManualTurnOn = turnOn;
            PendingManualModeExecution = true;
        }

        private (bool hasValidTemperature, double temp) GetOvenTemperatureFromVector(NewVectorReceivedArgs vector) 
        {
            var sensor = _config.OvenSensor;
            if (sensor == null) return (false, double.MaxValue);
            if (!TemperatureBoardsAreConnected(vector, _config.TemperatureBoardStateSensorNames)) return (false, double.MaxValue);
            if (!vector.TryGetValue(sensor, out var val))
                WarnMissingSensor(sensor);
            else if (val <= 10000 && val > 0) // we only use valid positive values. Note some temperature hubs alternate between 10k+ and 0 for errors.
                return (true, val);
            else if (val < 0)
                WarnNegativeTemperatures(sensor);
            return (false, double.MaxValue);
        }

        private bool TemperatureBoardsAreConnected(NewVectorReceivedArgs vector, IReadOnlyCollection<string> boardStateSensorNames)
        {
            foreach (var board in boardStateSensorNames)
            {
                if (!vector.TryGetValue(_config.SwitchBoardStateSensorName, out var state))
                    throw new InvalidOperationException($"missing temperature board's connection state: {_config.SwitchBoardStateSensorName}");
                if (state != (int)BaseSensorBox.ConnectionState.ReceivingValues)
                    return false;
            }

            return true;
        }

        private void WarnNegativeTemperatures(string sensorName) =>
            LowFrequencyWarning(
                timeSinceLastNegativeValuesWarning, 
                sensorName, 
                $"detected negative values in sensors for heater {this.Name()}. Confirm thermocouples cables are not inverted");
 
        private void WarnMissingSensor(string sensorName) =>
            LowFrequencyWarning(
                timeSinceLastMissingTemperatureWarning, 
                sensorName, 
                $"detected missing temperature sensor {sensorName} for heater {Name()}. Confirm the name is listed exactly as is in the plot.");
        
        private void LowFrequencyWarning(Stopwatch watch, string sensorName, string message)
        {
            if (watch.IsRunning && watch.Elapsed.Hours < 1) return;
            CALog.LogErrorAndConsoleLn(LogID.A,message);
            watch.Restart();
        }

        /// <returns><c>true</c> if timed out with invalid values, <c>false</c> if we are waiting for the timeout and <c>null</c> if <paramref name="hasValidSensors"/> was <c>true</c></returns>
        private bool? CheckInvalidValuesTimeout(bool hasValidSensors, int milliseconds, DateTime vectorTime)
        {
            if (hasValidSensors)
                invalidValuesStartedVectorTime = default;
            else if(invalidValuesStartedVectorTime == default)
                invalidValuesStartedVectorTime = vectorTime;

            return hasValidSensors ? default(bool?) : (vectorTime - invalidValuesStartedVectorTime).TotalMilliseconds >= milliseconds;
        }

        // resends the off command every 5 seconds as long as there is current.
        private bool MustResendOffCommand(double temp, double current, double switchboardOnOffState, DateTime vectorTime) 
        { 
            if (IsOn || vectorTime < LastOff.AddSeconds(5) || !CurrentIsOn(current)) return false;
            LogRepeatCommand("off", temp, current, switchboardOnOffState);
            return true;
        }
        private bool CurrentIsOn(double current) => current > _config.CurrentSensingNoiseTreshold;
        public string Name() => _config.Name;
        private void LogRepeatCommand(string command, double  temp, double current, double switchboardOnOffState) => 
            CALog.LogData(LogID.A, $"{command}.={Name()}-{temp:N0}, v#={current}, switch-on/off={switchboardOnOffState}{Environment.NewLine}");

        private bool TryGetSwitchboardInputsFromVector(
            NewVectorReceivedArgs vector, out double current, out double switchboardOnOffState)
        {
            if (!vector.TryGetValue(_config.SwitchBoardStateSensorName, out var state))
                throw new InvalidOperationException($"missing heater's board connection state: {_config.SwitchBoardStateSensorName}");
            if (state != (int)BaseSensorBox.ConnectionState.ReceivingValues)
            {
                current = switchboardOnOffState = 0;
                return false;
            }

            if (!vector.TryGetValue(_config.CurrentSensorName, out current))
                throw new InvalidOperationException($"missing heater current: {_config.CurrentSensorName}");
            if (!vector.TryGetValue(_config.SwitchboardOnOffSensorName, out switchboardOnOffState))
                throw new InvalidOperationException($"missing switchboard on/off state: {_config.SwitchboardOnOffSensorName}");
            return true;
        }

        private static Config ToConfig(IOconfHeater heater, IOconfOven oven)
        {
            var (area, ovenSensor, boardStateSensorNames) = GetOvenInfo(heater.Name, oven);
            return new Config
            {
                Area = area,
                OvenSensor = ovenSensor,
                TemperatureBoardStateSensorNames = boardStateSensorNames,
                MaxTemperature = heater.MaxTemperature,
                CurrentSensingNoiseTreshold = heater.CurrentSensingNoiseTreshold,
                Name = heater.Name,
                SwitchBoardStateSensorName = heater.BoardStateSensorName,
                CurrentSensorName = heater.CurrentSensorName,
                SwitchboardOnOffSensorName = heater.SwitchboardOnOffSensorName
            };
        }

        private static (int area, string ovenSensor, IReadOnlyCollection<string> boardStateSensorNames) GetOvenInfo(string heaterName, IOconfOven oven)
        {
            if (oven != null && oven.IsTemperatureSensorInitialized)
                return (oven.OvenArea, oven.TemperatureSensorName, oven.BoardStateSensorNames);
            if (oven == null)
                CALog.LogInfoAndConsoleLn(LogID.A, $"Warn: no oven configured for heater {heaterName}");
            else if (!oven.IsTemperatureSensorInitialized)
                CALog.LogErrorAndConsoleLn(LogID.A, $"Warn: disabled oven for heater {heaterName} - missing temperature board");

            return (-1, default, default);
        }

        public class Config
        {
            public int Area { get; set; } = -1; // -1 when not set
            public string OvenSensor { get; set; } // sensor inside the oven somewhere.
            public int MaxTemperature { get; set; }
            public double CurrentSensingNoiseTreshold { get; set; }
            public string Name { get; set; }
            public string SwitchBoardStateSensorName { get; set; }
            public string CurrentSensorName { get; set; }
            public string SwitchboardOnOffSensorName { get; set; }
            public IReadOnlyCollection<string> TemperatureBoardStateSensorNames { get; set; }
        }
    }
}
