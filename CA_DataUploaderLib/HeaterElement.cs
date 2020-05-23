using CA_DataUploaderLib.IOconf;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CA_DataUploaderLib
{
    public class HeaterElement
    {
        private int OvenTargetTemperature;

        public IOconfHeater _ioconf;
        private int _area;
        private List<SensorSample> _ovenSensors = new List<SensorSample>();
        private List<SensorSample> _heaterSensors = new List<SensorSample>();
        public DateTime LastOn = DateTime.UtcNow.AddSeconds(-20); // assume nothing happened in the last 20 seconds
        public DateTime LastOff = DateTime.UtcNow.AddSeconds(-20); // assume nothing happened in the last 20 seconds
        private double onTemperature = 10000;
        public double lastTemperature = 10000;
        public bool IsOn;
        public bool ManualMode;
        public double Current;  // Amps per element. 
        public bool IsActive { get { return OvenTargetTemperature > 0;  } }

        public HeaterElement(int area, IOconfHeater heater, IEnumerable<SensorSample> heaterSensors, IEnumerable<SensorSample> ovenSensors)
        {
            _ioconf = heater;
            _area = area;
            _heaterSensors = heaterSensors.ToList();
            _ovenSensors = ovenSensors.ToList();
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
            var validSensors = _ovenSensors.Where(x => x.TimeStamp > twoSecAgo && x.Value < 6000);
            if (!validSensors.Any())
                return false;  // no valid oven sensors 

            if (validSensors.Any(x => x.Value > OvenTargetTemperature))
                return false;  // at least one of the temperature sensors value is valid and above OvenTargetTemperature.  

            var heaterSensors = _heaterSensors.Where(x => x.TimeStamp > twoSecAgo && x.Value < 6000);
            if (!heaterSensors.Any() && _heaterSensors.Any())
                return false;  // no valid heater sensors 

            if (heaterSensors.Any() && LastOff.AddSeconds(_ioconf.MaxOnInterval) > DateTime.UtcNow)
                return false;

            if (heaterSensors.Any(x => x.Value > _ioconf.MaxTemperature))
                return false;  // at least one of the temperature sensors value is valid and above the heating element MaxTemperature.  

            onTemperature = validSensors.Max(x => x.Value);
            return true;
        }

        public bool MustTurnOff()
        {
            var twoSecAgo = DateTime.UtcNow.AddSeconds(-2);
            var validSensors = _ovenSensors.Where(x => x.TimeStamp > twoSecAgo && x.Value < 6000);
            if (!validSensors.Any())
                return true; // no valid oven sensors

            var heaterSensors = _heaterSensors.Where(x => x.TimeStamp > twoSecAgo && x.Value < 6000);
            if (!heaterSensors.Any() && _heaterSensors.Any())
                return true; // no valid heater sensors

            if (heaterSensors.Any(x => x.Value > _ioconf.MaxTemperature))
                return true; // element must never reach above the heating element MaxTemperature. 

            if (ManualMode)
                return false;

            if (onTemperature < 10000 && validSensors.Max(x => x.Value) > onTemperature + 20)
                return true; // If hottest sensor is 20C higher than the temperature last time we turned on, then turn off. 

            var turnOff = validSensors.Any(x => x.Value > OvenTargetTemperature); // turn off, if we reached OvenTargetTemperature. 
            if(!turnOff)
                lastTemperature = validSensors.Max(x => x.Value);

            return turnOff;
        }

        public double MaxSensorTemperature()
        {
            var twoSecAgo = DateTime.UtcNow.AddSeconds(-2);
            var validSensors = _ovenSensors.Where(x => x.TimeStamp > twoSecAgo && x.Value < 6000);
            if(validSensors.Any())
                return validSensors.Max(x => x.Value);

            return _ovenSensors.First().Value;
        }

        public bool IsArea(int ovenArea)
        {
            return _area == ovenArea;
        }

        public override string ToString()
        {
            string msg = string.Empty;
            foreach (var s in _ovenSensors)
                msg += s.Value.ToString("N0") + ", " + (LastOn > LastOff ? "" : onTemperature.ToString("N0"));

            return $"{_ioconf.Name.PadRight(10)} is {(LastOn > LastOff ? "ON,  " : "OFF, ")}{msg.PadRight(12)} {Current.ToString("N1").PadRight(5)} Amp";
        }

        public string name()
        {
            return _ioconf.Name.ToLower();
        }

        public MCUBoard Board()
        {
            return _ioconf.Map.Board;
        }
    }
}
