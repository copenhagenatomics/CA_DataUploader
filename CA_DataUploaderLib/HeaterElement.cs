using CA_DataUploaderLib.IOconf;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace CA_DataUploaderLib
{
    public class HeaterElement
    {
        private int OvenTargetTemperature;

        public IOconfHeater _ioconf;
        private readonly int _area;  // -1 if not defined. 
        private readonly List<SensorSample> _ovenSensors = new List<SensorSample>();    // sensors inside the oven somewhere.
        public DateTime LastOn = DateTime.UtcNow.AddSeconds(-20); // assume nothing happened in the last 20 seconds
        public DateTime LastOff = DateTime.UtcNow.AddSeconds(-20); // assume nothing happened in the last 20 seconds
        public readonly Stopwatch invalidValuesTime = new Stopwatch();
        private double onTemperature = 10000;
        public double lastTemperature = 10000;
        public bool IsOn;
        public bool ManualMode;
        public SensorSample Current;  // Amps per element. 
        public double SwitchboardOnState;  //same as IsOn, but reported by the switchboard (null for a switchboard not reporting state)
        public bool IsActive { get { return OvenTargetTemperature > 0;  } }
        private Stopwatch timeSinceLastNegativeValuesWarning = new Stopwatch();

        public HeaterElement(int area, IOconfHeater heater, IEnumerable<SensorSample> ovenSensors)
        {
            _ioconf = heater;
            _area = area;
            _ovenSensors = ovenSensors.ToList();
            Current = new SensorSample(heater.AsConfInput());
        }

        public void SetTemperature(int value)
        {
            OvenTargetTemperature = Math.Min(value, _ioconf.MaxTemperature);
        }

        public bool CanTurnOn()
        {
            if (IsOn) return false;
            if (ManualMode) return false;

            if (LastOff > DateTime.UtcNow.AddSeconds(-10))
                return false;  // has been turned off for less than 10 seconds. 

            var twoSecAgo = DateTime.UtcNow.AddSeconds(-2);
            var validSensors = GetValidSensorsSnapshot(twoSecAgo);
            if (!validSensors.Any())
                return false;  // no valid oven sensors

            if (validSensors.Any(x => x.Value >= OvenTargetTemperature))
                return false;  // at least one of the temperature sensors value is valid and above OvenTargetTemperature.  

            return SetOnProperties(validSensors);
        }

        public bool MustTurnOff()
        {
            if (!IsOn) return false;
            var twoSecAgo = DateTime.UtcNow.AddSeconds(-2);
            var validSensors = GetValidSensorsSnapshot(twoSecAgo).ToList();
            var timeoutResult = CheckInvalidValuesTimeout(validSensors.Any(), 2000);
            if (timeoutResult.HasValue && timeoutResult.Value)
                return SetOffProperties();
            else if (timeoutResult.HasValue)
                return false;

            if (ManualMode)
                return false;

            if (onTemperature < 10000 && validSensors.Max(x => x.Value) > onTemperature + 20)
                return SetOffProperties(); // If hottest sensor is 20C higher than the temperature last time we turned on, then turn off. 

            if (validSensors.Any(x => x.Value > OvenTargetTemperature)) 
                return SetOffProperties(); //if we reached OvenTargetTemperature, then turn off.

            lastTemperature = validSensors.Max(x => x.Value);
            return false;
        }

        // returns true to simplify MustTurnOff
        private bool SetOffProperties()
        {
            IsOn = false;
            LastOff = DateTime.UtcNow;
            return true;
        }

        // returns true to simplify MustTurnOn
        private bool SetOnProperties(IEnumerable<SensorSample> validSensors)
        {
            onTemperature = validSensors.Max(x => x.Value);
            IsOn = true;
            LastOn = DateTime.UtcNow;
            return true;
        }

        public void SetManualMode(bool turnOn) => ManualMode = IsOn = turnOn;
        private List<SensorSample> GetValidSensorsSnapshot(DateTime twoSecAgo) 
        {
            var snapshot = _ovenSensors.Where(x => x.TimeStamp > twoSecAgo && x.Value < 10000 && x.Value != 0).Select(s => s.Clone()).ToList();
            if (snapshot.Count == 0)
                return snapshot;

            if (snapshot.RemoveAll(s => s.Value < 0) > 0 && 
                (!timeSinceLastNegativeValuesWarning.IsRunning || timeSinceLastNegativeValuesWarning.Elapsed.Hours >= 1))
            {
                CALog.LogErrorAndConsoleLn(
                    LogID.A, 
                    $"detected negative values in sensors for heater {this.Name()}. Confirm thermocouples cables are not inverted");
                timeSinceLastNegativeValuesWarning.Restart();
            }

            return snapshot;
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

        public double MaxSensorTemperature()
        {
            var twoSecAgo = DateTime.UtcNow.AddSeconds(-2);
            var validSensors = _ovenSensors.Where(x => x.TimeStamp > twoSecAgo && x.Value < 6000);
            if(validSensors.Any())
                return validSensors.Max(x => x.Value);

            if (!_ovenSensors.Any())
                return 0;

            return _ovenSensors.First().Value;
        }

        public bool IsArea(int ovenArea)
        {
            return _area == ovenArea;
        }

        // resends the on command every 5 seconds as there is no current or signs of not reaching the switchbox.
        public bool MustResendOnCommand()
        { 
            if (!IsOn || DateTime.UtcNow < LastOn.AddSeconds(5)) return false;
            if (CurrentIsOn() || IsCurrentStale()) return false; 
            LogRepeatCommand("on");
            return true;
        }

        // resends the off command every 5 seconds as long as there is current or signs of not reaching the switchbox.
        public bool MustResendOffCommand() 
        { 
            if (IsOn || DateTime.UtcNow < LastOff.AddSeconds(5)) return false;
            if (!CurrentIsOn() && !IsCurrentStale()) return false; 
            LogRepeatCommand("off");
            return true;
        }
        private bool CurrentIsOn() => Current.Value > _ioconf.CurrentSensingNoiseTreshold;
        private bool IsCurrentStale() => Current.TimeStamp < DateTime.UtcNow.AddSeconds(-10);

        public override string ToString()
        {
            string msg = string.Empty;
            foreach (var s in _ovenSensors)
                msg += s.Value.ToString("N0") + ", " + (LastOn > LastOff ? "" : onTemperature.ToString("N0"));

            return $"{_ioconf.Name,-10} is {(LastOn > LastOff ? "ON,  " : "OFF, ")}{msg,-12} {Current.Value,-5:N1} Amp";
        }

        public string Name() => _ioconf.Name.ToLower();

        public MCUBoard Board() => _ioconf.Map.Board;
        private void LogRepeatCommand(string command) => 
            CALog.LogData(LogID.A, $"{command}.={Name()}-{MaxSensorTemperature():N0}, v#={Current}, switch-on/off={SwitchboardOnState}, currents-time={Current.TimeStamp} WB={Board().BytesToWrite}{Environment.NewLine}");
    }
}
