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
        public int PortNumber => _ioconf.PortNumber;
        private int OvenTargetTemperature;
        private IOconfHeater _ioconf;
        private readonly int _area = -1;  // -1 if not defined. 
        private readonly string _ovenSensor; // sensor inside the oven somewhere.
        private DateTime LastOn = DateTime.UtcNow.AddSeconds(-20); // assume nothing happened in the last 20 seconds
        private DateTime LastOff = DateTime.UtcNow.AddSeconds(-20); // assume nothing happened in the last 20 seconds
        private readonly Stopwatch invalidValuesTime = new Stopwatch();
        private DateTime invalidValuesStartedVectorTime = default;
        private double onTemperature = 10000;
        public bool IsOn;
        private bool ManualMode;
        public bool IsActive { get { return OvenTargetTemperature > 0;  } }
        private Stopwatch timeSinceLastNegativeValuesWarning = new Stopwatch();
        private Stopwatch timeSinceLastMissingTemperatureWarning = new Stopwatch();
        private SwitchboardAction _lastAction = new SwitchboardAction(false, DateTime.UtcNow);

        public HeaterElement(IOconfHeater heater, IOconfOven oven)
        {
            _ioconf = heater;
            if (oven == null)
                CALog.LogInfoAndConsoleLn(LogID.A, $"Warn: no oven configured for heater {heater.Name}");
            else if (!oven.IsTemperatureSensorInitialized)
                CALog.LogErrorAndConsoleLn(LogID.A, $"Warn: disabled oven for heater {heater.Name} - missing temperature board");
            else
            {
                _area = oven.OvenArea;
                _ovenSensor = oven.TemperatureSensorName;
            }
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
            var action =
                MustTurnOff(hasValidTemperature, temp, vectorTime) ? new SwitchboardAction(false, vectorTime):
                CanTurnOn(hasValidTemperature, temp, vectorTime) ? new SwitchboardAction(true, vectorTime.AddSeconds(60)) : //TODO: short time outs?
                MustResendOnCommand(temp, current, switchboardOnOffState, vectorTime) ? new SwitchboardAction(true, vectorTime.AddSeconds(60)): //TODO: short time outs? also we should not extend time to turn off here ...
                MustResendOffCommand(temp, current, switchboardOnOffState, vectorTime) ? new SwitchboardAction(false, vectorTime) :
                _lastAction;
            return _lastAction = action;
        }

        public void SetTargetTemperature(int value) => OvenTargetTemperature = Math.Min(value, _ioconf.MaxTemperature);
        public void SetTargetTemperature(IEnumerable<(int area, int temperature)> values)
        {
            if (_area == -1) return; // temperature control is not enabled for the heater (no oven or missing temperature hub)

            foreach (var (area, temperature) in values)
                if (_area == area)
                    SetTargetTemperature(temperature);
        }

        private bool CanTurnOn(bool hasValidTemperature, double temperature, DateTime vectorTime)
        {
            if (IsOn) return false; // already on
            if (ManualMode) return false; // avoid auto on when manual mode is on.
            if (OvenTargetTemperature <= 0) return false; // oven's command is off, skip any extra checks
            if (!hasValidTemperature) return false; // no valid oven sensors

            if (LastOff > vectorTime.AddSeconds(-10))
                return false;  // less than 10 seconds since we last turned it off

            if (temperature >= OvenTargetTemperature)
                return false; // already at target temperature. 

            onTemperature = temperature;
            return SetOnProperties(vectorTime);
        }

        private bool MustTurnOff(bool hasValidTemperature, double temperature, DateTime vectorTime)
        {
            if (!IsOn) return false; // already off
            if (OvenTargetTemperature <= 0 && !ManualMode)
                return SetOffProperties(vectorTime); // turn off: oven's command is off, skip any extra checks
            var timeoutResult = CheckInvalidValuesTimeout(hasValidTemperature, 2000, vectorTime);
            if (timeoutResult.HasValue && timeoutResult.Value)
                return SetOffProperties(vectorTime); // turn off: 2 seconds without valid temperatures (even if running with manual mode)
            else if (timeoutResult.HasValue)
                return false; // no valid temperatures, waiting up to 2 seconds before turn off

            if (ManualMode) 
                return false; // avoid auto on/off when running in manual mode (except the 2 seconds without valid reads earlier)

            if (onTemperature < 10000 && temperature > onTemperature + 20)
                return SetOffProperties(vectorTime); // turn off: already 20C higher than the last time we turned on

            if (temperature > OvenTargetTemperature)
                return SetOffProperties(vectorTime); //turn off: already at target temperature

            return false;
        }

        // returns true to simplify MustTurnOff
        private bool SetOffProperties(DateTime vectorTime)
        {
            IsOn = false;
            LastOff = vectorTime;
            return true;
        }

        private bool SetOnProperties(DateTime vectorTime)
        {
            IsOn = true;
            LastOn = vectorTime;
            return true;
        }

        public void SetManualMode(bool turnOn)
        { 
            var vectorTime = DateTime.UtcNow; // note: when running distributed decisions this needs to be the logical time like we have in the vector.
            ManualMode = turnOn;
            if (turnOn)
                SetOnProperties(vectorTime);
            else
                SetOffProperties(vectorTime);
        }

        private (bool hasValidTemperature, double temp) GetOvenTemperatureFromVector(NewVectorReceivedArgs vector) 
        {
            if (_ovenSensor == null) return (false, double.MaxValue);
            if (!vector.TryGetValue(_ovenSensor, out var val))
                WarnMissingSensor(_ovenSensor);
            else if (val <= 10000 && val > 0) // we only use valid positive values. Note some temperature hubs alternate between 10k+ and 0 for errors.
                return (true, val);
            else if (val < 0)
                WarnNegativeTemperatures(_ovenSensor);
            return (false, double.MaxValue);
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

        // resends the on command every 5 seconds as long as there is no current.
        private bool MustResendOnCommand(double temp, double current, double switchboardOnOffState, DateTime vectorTime)
        { 
            if (!IsOn || vectorTime < LastOn.AddSeconds(5) || CurrentIsOn(current)) return false;
            LogRepeatCommand("on", temp, current, switchboardOnOffState);
            return true;
        }

        // resends the off command every 5 seconds as long as there is current.
        private bool MustResendOffCommand(double temp, double current, double switchboardOnOffState, DateTime vectorTime) 
        { 
            if (IsOn || vectorTime < LastOff.AddSeconds(5) || !CurrentIsOn(current)) return false;
            LogRepeatCommand("off", temp, current, switchboardOnOffState);
            return true;
        }
        private bool CurrentIsOn(double current) => current > _ioconf.CurrentSensingNoiseTreshold;
        public string Name() => _ioconf.Name.ToLower();
        public MCUBoard Board() => _ioconf.Map.Board;
        private void LogRepeatCommand(string command, double  temp, double current, double switchboardOnOffState) => 
            CALog.LogData(LogID.A, $"{command}.={Name()}-{temp:N0}, v#={current}, switch-on/off={switchboardOnOffState}, WB={Board().BytesToWrite}{Environment.NewLine}");

        private bool TryGetSwitchboardInputsFromVector(
            NewVectorReceivedArgs vector, out double current, out double switchboardOnOffState)
        {
            if (!vector.TryGetValue(_ioconf.BoardStateSensorName, out var state))
                throw new InvalidOperationException($"missing heater's board connection state: {_ioconf.BoardStateSensorName}");
            if (state != (int)BaseSensorBox.ConnectionState.Connected)
            {
                current = switchboardOnOffState = 0;
                return false;
            }

            if (!vector.TryGetValue(_ioconf.CurrentSensorName, out current))
                throw new InvalidOperationException($"missing heater current: {_ioconf.CurrentSensorName}");
            if (!vector.TryGetValue(_ioconf.SwitchboardOnOffSensorName, out switchboardOnOffState))
                throw new InvalidOperationException($"missing switchboard on/off state: {_ioconf.SwitchboardOnOffSensorName}");
            return true;
        }
    }
}
