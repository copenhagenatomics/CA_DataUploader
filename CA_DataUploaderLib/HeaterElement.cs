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
        public bool IsActive { get { return OvenTargetTemperature > 0;  } }

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
            if (ManualMode)
                return false;

            if (LastOff > DateTime.UtcNow.AddSeconds(-10))
                return false;  // has been turned off for less than 10 seconds. 

            var twoSecAgo = DateTime.UtcNow.AddSeconds(-2);
            var validSensors = _ovenSensors.Select(s => s.Clone()).Where(x => x.TimeStamp > twoSecAgo && x.Value < 10000).ToList();
            if (!validSensors.Any())
                return false;  // no valid oven sensors 

            if (validSensors.Any(x => x.Value >= OvenTargetTemperature))
                return false;  // at least one of the temperature sensors value is valid and above OvenTargetTemperature.  

            onTemperature = validSensors.Max(x => x.Value);
            return true;
        }

        public bool MustTurnOff()
        {
            var twoSecAgo = DateTime.UtcNow.AddSeconds(-2);
            var validSensors = _ovenSensors.Select(s => s.Clone()).Where(x => x.TimeStamp > twoSecAgo && x.Value < 10000).ToList();
            var timeoutResult = CheckInvalidValuesTimeout(validSensors.Any(), 2000);
            if (timeoutResult.HasValue)
                return timeoutResult.Value;

            if (ManualMode)
                return false;

            if (onTemperature < 10000 && validSensors.Max(x => x.Value) > onTemperature + 20)
                return true; // If hottest sensor is 20C higher than the temperature last time we turned on, then turn off. 

            var turnOff = validSensors.Any(x => x.Value > OvenTargetTemperature); // turn off, if we reached OvenTargetTemperature. 
            if(!turnOff)
                lastTemperature = validSensors.Max(x => x.Value);

            return turnOff;
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

        public bool MustResendOnCommand() => !CurrentIsOn() && IsOn && LastOn.AddSeconds(2) < DateTime.UtcNow;
        public bool MustResendOffCommand() => CurrentIsOn() && !IsOn && LastOff.AddSeconds(2) < DateTime.UtcNow;
        private bool CurrentIsOn() => Current.Value > _ioconf.CurrentSensingNoiseTreshold;

        public override string ToString()
        {
            string msg = string.Empty;
            foreach (var s in _ovenSensors)
                msg += s.Value.ToString("N0") + ", " + (LastOn > LastOff ? "" : onTemperature.ToString("N0"));

            return $"{_ioconf.Name,-10} is {(LastOn > LastOff ? "ON,  " : "OFF, ")}{msg,-12} {Current.Value,-5:N1} Amp";
        }

        public string Name() => _ioconf.Name.ToLower();

        public MCUBoard Board() => _ioconf.Map.Board;
    }
}
