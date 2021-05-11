using CA.LoopControlPluginBase;
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
        private readonly int _area;  // -1 if not defined. 
        private readonly List<string> _ovenSensors;    // sensors inside the oven somewhere.
        private DateTime LastOn = DateTime.UtcNow.AddSeconds(-20); // assume nothing happened in the last 20 seconds
        private DateTime LastOff = DateTime.UtcNow.AddSeconds(-20); // assume nothing happened in the last 20 seconds
        private readonly Stopwatch invalidValuesTime = new Stopwatch();
        private double onTemperature = 10000;
        public bool IsOn;
        private bool ManualMode;
        public bool IsActive { get { return OvenTargetTemperature > 0;  } }
        private Stopwatch timeSinceLastNegativeValuesWarning = new Stopwatch();

        public HeaterElement(int area, IOconfHeater heater, IEnumerable<string> ovenSensors)
        {
            _ioconf = heater;
            _area = area;
            _ovenSensors = new List<string>(ovenSensors);
        }

        public HeaterAction MakeNextActionDecision(NewVectorReceivedArgs vector)
        {
            if (!TryGetSwitchboardInputsFromVector(vector, out var current, out var switchboardOnOffState)) 
                return HeaterAction.None; // not connected, we skip this heater and act again when the connection is re-established
            var (hasValidTemperatures, maxTemp) = GetMaxTemperatureInTargetSensors(vector);
            // Careful consideration must be taken if changing the order of the below statements.
            // Note that even though we received indication the board is connected above, 
            // if the connection is lost after we return the action, the control program can still fail to act on the heater. 
            // When it happens, the MustResend* methods will resend the expected action after 5 seconds.
            return 
                MustTurnOff(hasValidTemperatures, maxTemp) ? HeaterAction.TurnOff :
                CanTurnOn(hasValidTemperatures, maxTemp) ? HeaterAction.TurnOn :
                MustResendOnCommand(maxTemp, current, switchboardOnOffState) ? HeaterAction.TurnOn : 
                MustResendOffCommand(maxTemp, current, switchboardOnOffState) ? HeaterAction.TurnOff :
                HeaterAction.None;
        }

        public void SetTargetTemperature(int value)
        {
            OvenTargetTemperature = Math.Min(value, _ioconf.MaxTemperature);
        }

        public bool CanTurnOn(bool hasValidTemperatures, double maxTemperatureInTargetSensors)
        {
            if (IsOn) return false; // already on
            if (ManualMode) return false; // avoid auto on/off when running in manual mode
            if (OvenTargetTemperature <= 0) return false; // oven's command is off, skip any extra checks
            if (hasValidTemperatures) return false; // no valid oven sensors

            if (LastOff > DateTime.UtcNow.AddSeconds(-10))
                return false;  // less than 10 seconds since we last turned it off

            if (maxTemperatureInTargetSensors >= OvenTargetTemperature)
                return false; // already at target temperature. 

            onTemperature = maxTemperatureInTargetSensors;
            return SetOnProperties();
        }

        public bool MustTurnOff(bool hasValidTemperatures, double maxTemperatureInTargetSensors)
        {
            if (!IsOn) return false; // already off
            if (OvenTargetTemperature <= 0 && !ManualMode)
                return SetOffProperties(); // turn off: oven's command is off, skip any extra checks
            var timeoutResult = CheckInvalidValuesTimeout(hasValidTemperatures, 2000);
            if (timeoutResult.HasValue && timeoutResult.Value)
                return SetOffProperties(); // turn off: 2 seconds without valid temperatures (even if running with manual mode)
            else if (timeoutResult.HasValue)
                return false; // no valid temperatures, waiting up to 2 seconds before turn off

            if (ManualMode) 
                return false; // avoid auto on/off when running in manual mode (except the 2 seconds without valid reads earlier)

            if (onTemperature < 10000 && maxTemperatureInTargetSensors > onTemperature + 20)
                return SetOffProperties(); // turn off: already 20C higher than the last time we turned on

            if (maxTemperatureInTargetSensors > OvenTargetTemperature)
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

        private (bool hasValidTemperatures, double maxTemp) GetMaxTemperatureInTargetSensors(NewVectorReceivedArgs vector) 
        {
            Span<double> values = stackalloc double[_ovenSensors.Count];
            int end = 0;
            foreach (var sensorName in _ovenSensors)
            {
                if (vector.TryGetValue(sensorName, out var val))
                    throw new InvalidOperationException($"missing temperature for oven control: {sensorName}");
                if (val <= 10000 && val > 0) // we only use valid positive values. Note some temperature hubs alternate between 10k+ and 0 for errors.
                    values[end++] = val;
                else if (val < 0)
                    WarnNegativeTemperatures(sensorName, val);
            }

            values = values.Slice(0, end);
            var hasValidTemperatures = !values.IsEmpty;
            var maxTemp = hasValidTemperatures ? Max(values) : double.MaxValue;
            return (hasValidTemperatures, maxTemp);
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
        public bool MustResendOnCommand(double maxTemp, double current, double switchboardOnOffState)
        { 
            if (!IsOn || DateTime.UtcNow < LastOn.AddSeconds(5) || CurrentIsOn(current)) return false;
            LogRepeatCommand("on", maxTemp, current, switchboardOnOffState);
            return true;
        }

        // resends the off command every 5 seconds as long as there is current.
        public bool MustResendOffCommand(double maxTemp, double current, double switchboardOnOffState) 
        { 
            if (IsOn || DateTime.UtcNow < LastOff.AddSeconds(5) || !CurrentIsOn(current)) return false;
            LogRepeatCommand("off", maxTemp, current, switchboardOnOffState);
            return true;
        }
        private bool CurrentIsOn(double current) => current > _ioconf.CurrentSensingNoiseTreshold;
        public string Name() => _ioconf.Name.ToLower();
        public MCUBoard Board() => _ioconf.Map.Board;
        private void LogRepeatCommand(string command, double  maxTemp, double current, double switchboardOnOffState) => 
            CALog.LogData(LogID.A, $"{command}.={Name()}-{maxTemp:N0}, v#={current}, switch-on/off={switchboardOnOffState}, WB={Board().BytesToWrite}{Environment.NewLine}");

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
