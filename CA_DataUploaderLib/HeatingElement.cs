using CA_DataUploaderLib.IOconf;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CA_DataUploaderLib
{
    public class HeaterElement
    {
        public IOconfHeater ioconf;
        public List<SensorSample> sensors = new List<SensorSample>();
        public DateTime LastOn = DateTime.UtcNow.AddSeconds(-20); // assume nothing happened in the last 20 seconds
        public DateTime LastOff = DateTime.UtcNow.AddSeconds(-20); // assume nothing happened in the last 20 seconds
        public double onTemperature = 10000;
        public bool IsOn;
        public bool ManualMode;
        public double Current;  // Amps per element. 
        public int OffsetSetTemperature = 0;

        public HeaterElement(IOconfHeater heater)
        {
            ioconf = heater;
        }

        public bool CanTurnOn(int maxTemperature)
        {
            if (ManualMode)
                return false;

            if (LastOff > DateTime.UtcNow.AddSeconds(-10))
                return false;  // has been turned off for less than 10 seconds. 

            var validSensors = sensors.Where(x => x.TimeStamp > DateTime.UtcNow.AddSeconds(-2) && x.Value < 6000);
            if (!validSensors.Any())
                return false;  // no valid sensors 

            if (validSensors.Any(x => x.Value > (maxTemperature + OffsetSetTemperature)))
                return false;  // at least one of the temperature sensors value is valid and above maxTemperature.  

            onTemperature = validSensors.Max(x => x.Value);
            return true;
        }

        public bool MustTurnOff(int maxTemperature)
        {
            var validSensors = sensors.Where(x => x.TimeStamp > DateTime.UtcNow.AddSeconds(-2) && x.Value < 6000);
            if (!validSensors.Any())
                return true; // no valid sensors

            if (maxTemperature == 0 && OffsetSetTemperature == 0 && ManualMode)
                return false;

            if (onTemperature < 10000 && validSensors.Max(x => x.Value) > onTemperature + 20)
                return true; // If hottest sensor is 50C higher than the temperature last time we turned on, then turn off. 

            return validSensors.Any(x => x.Value > (maxTemperature + OffsetSetTemperature)); // turn off, if we reached maxTemperature. 
        }

        public override string ToString()
        {
            string msg = string.Empty;
            foreach (var s in sensors)
                msg += s.Value.ToString("N0") + ", " + (LastOn > LastOff ? "" : onTemperature.ToString("N0"));

            return $"{ioconf.Name.PadRight(10)} is {(LastOn > LastOff ? "ON,  " : "OFF, ")}{msg.PadRight(12)} {Current.ToString("N1").PadRight(5)} Amp";
        }

        public string name()
        {
            return ioconf.Name.ToLower();
        }

        public MCUBoard Board()
        {
            return ioconf.Map.Board;
        }
    }
}
