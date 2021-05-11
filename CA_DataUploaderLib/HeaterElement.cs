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
        private double onTemperature = 10000;
        public bool IsOn;
        private bool ManualMode;
        public bool IsActive { get { return OvenTargetTemperature > 0;  } }
        private Stopwatch timeSinceLastNegativeValuesWarning = new Stopwatch();

        public HeaterElement(IOconfHeater heater, IOconfOven oven)
        {
            _ioconf = heater;
            if (oven == null)
                CALog.LogInfoAndConsoleLn(LogID.A, $"Warn: no oven configured for heater {heater.Name}"));
            else if (!oven.TypeK.IsInitialized())
                CALog.LogErrorAndConsoleLn(LogID.A, $"Warn: disabled oven for heater {heater.Name} - missing temperature board");
            else
            {
                _area = oven.OvenArea;
                _ovenSensor = oven.TypeK.Name;
            }
        }

        public HeaterAction MakeNextActionDecision(NewVectorReceivedArgs vector)
        {
            if (!TryGetSwitchboardInputsFromVector(vector, out var current, out var switchboardOnOffState)) 
                return HeaterAction.None; // not connected, we skip this heater and act again when the connection is re-established
            var (hasValidTemperature, temp) = GetOvenTemperatureFromVector(vector);
            // Careful consideration must be taken if changing the order of the below statements.
            // Note that even though we received indication the board is connected above, 
            // if the connection is lost after we return the action, the control program can still fail to act on the heater. 
            // When it happens, the MustResend* methods will resend the expected action after 5 seconds.
            return 
                MustTurnOff(hasValidTemperature, temp) ? HeaterAction.TurnOff :
                CanTurnOn(hasValidTemperature, temp) ? HeaterAction.TurnOn :
                MustResendOnCommand(temp, current, switchboardOnOffState) ? HeaterAction.TurnOn : 
                MustResendOffCommand(temp, current, switchboardOnOffState) ? HeaterAction.TurnOff :
                HeaterAction.None;
        }

        public void SetTargetTemperature(int value)
        {
            OvenTargetTemperature = Math.Min(value, _ioconf.MaxTemperature);
        }

        public bool CanTurnOn(bool hasValidTemperature, double temperature)
        {
            if (IsOn) return false; // already on
            if (ManualMode) return false; // avoid auto on when manual mode is on.
            if (OvenTargetTemperature <= 0) return false; // oven's command is off, skip any extra checks
            if (hasValidTemperature) return false; // no valid oven sensors

            if (LastOff > DateTime.UtcNow.AddSeconds(-10))
                return false;  // less than 10 seconds since we last turned it off

            if (temperature >= OvenTargetTemperature)
                return false; // already at target temperature. 

            onTemperature = temperature;
            return SetOnProperties();
        }

        public bool MustTurnOff(bool hasValidTemperature, double temperature)
        {
            if (!IsOn) return false; // already off
            if (OvenTargetTemperature <= 0 && !ManualMode)
                return SetOffProperties(); // turn off: oven's command is off, skip any extra checks
            var timeoutResult = CheckInvalidValuesTimeout(hasValidTemperature, 2000);
            if (timeoutResult.HasValue && timeoutResult.Value)
                return SetOffProperties(); // turn off: 2 seconds without valid temperatures (even if running with manual mode)
            else if (timeoutResult.HasValue)
                return false; // no valid temperatures, waiting up to 2 seconds before turn off

            if (ManualMode) 
                return false; // avoid auto on/off when running in manual mode (except the 2 seconds without valid reads earlier)

            if (onTemperature < 10000 && temperature > onTemperature + 20)
                return SetOffProperties(); // turn off: already 20C higher than the last time we turned on

            if (temperature > OvenTargetTemperature)
                return SetOffProperties(); //turn off: already at target temperature

            return false;
        }

        // returns true to simplify MustTurnOff
        private bool SetOffProperties()
        {
            IsOn = false;
            LastOff = DateTime.UtcNow;
            return true;
        }

        private bool SetOnProperties()
        {
            IsOn = true;
            LastOn = DateTime.UtcNow;
            return true;
        }

        public void SetManualMode(bool turnOn)
        { 
            ManualMode = IsOn = turnOn;
            if (turnOn)
                SetOnProperties();
            else
                SetOffProperties();
        }

        private double Max(Span<double> values) 
        {
            var val = values[0];
            for (int i = 1; i < values.Length; i++)
                if (values[i] > val)
                    val = values[i];
            return val;
        }

        private (bool hasValidTemperature, double temp) GetOvenTemperatureFromVector(NewVectorReceivedArgs vector) 
        {
            if (_ovenSensor == null) return (false, double.MaxValue);
            if (vector.TryGetValue(_ovenSensor, out var val))
                throw new InvalidOperationException($"missing temperature for oven control: {_ovenSensor}");
            if (val <= 10000 && val > 0) // we only use valid positive values. Note some temperature hubs alternate between 10k+ and 0 for errors.
                return (true, val);
            else if (val < 0)
                WarnNegativeTemperatures(_ovenSensor, val);
            return (false, double.MaxValue);
        }

        private void WarnNegativeTemperatures(string name, double val)
        {
            if (timeSinceLastNegativeValuesWarning.IsRunning && timeSinceLastNegativeValuesWarning.Elapsed.Hours < 1)
                return;
            CALog.LogErrorAndConsoleLn(
                LogID.A,
                $"detected negative values in sensors for heater {this.Name()}. Confirm thermocouples cables are not inverted");
            timeSinceLastNegativeValuesWarning.Restart();
        }

        /// <returns><c>true</c> if timed out with invalid values, <c>false</c> if we are waiting for the timeout and <c>null</c> if <paramref name="hasValidSensors"/> was <c>true</c></returns>
        private bool? CheckInvalidValuesTimeout(bool hasValidSensors, int milliseconds)
        {
            if (hasValidSensors)
                invalidValuesTime.Reset();
            else if(!invalidValuesTime.IsRunning)
                invalidValuesTime.Restart();

            return hasValidSensors ? default(bool?) : invalidValuesTime.ElapsedMilliseconds >= milliseconds;
        }

        public bool IsArea(int ovenArea) => _area == ovenArea;
        // resends the on command every 5 seconds as long as there is no current.
        public bool MustResendOnCommand(double temp, double current, double switchboardOnOffState)
        { 
            if (!IsOn || DateTime.UtcNow < LastOn.AddSeconds(5) || CurrentIsOn(current)) return false;
            LogRepeatCommand("on", temp, current, switchboardOnOffState);
            return true;
        }

        // resends the off command every 5 seconds as long as there is current.
        public bool MustResendOffCommand(double temp, double current, double switchboardOnOffState) 
        { 
            if (IsOn || DateTime.UtcNow < LastOff.AddSeconds(5) || !CurrentIsOn(current)) return false;
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

    public enum HeaterAction
    {
        None,
        TurnOff,
        TurnOn
    }
}
