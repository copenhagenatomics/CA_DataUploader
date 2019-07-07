using System;
using System.Collections.Generic;
using System.Linq;

namespace CA_DataUploaderLib
{
    public class HeaterElement
    {
        public List<TermoSensor> sensors = new List<TermoSensor>();
        public string SwitchBoard;  // the serial number of the Switch box. 
        public int port; // the port number of the switch box.
        public DateTime LastOn = DateTime.UtcNow.AddSeconds(-20); // assume nothing happened in the last 20 seconds
        public DateTime LastOff = DateTime.UtcNow.AddSeconds(-20); // assume nothing happened in the last 20 seconds
        public double onTemperature = 10000;
        public bool IsOn;
        public int ExtendMaxTemperature = 0;

        public bool CanTurnOn(int maxTemperature)
        {
            if (LastOff > DateTime.UtcNow.AddSeconds(-10))
                return false;  // has been turned off for less than 20 seconds. 

            var validSensors = sensors.Where(x => x.TimeStamp > DateTime.UtcNow.AddSeconds(-2) && x.Temperature < 6000);
            if (!validSensors.Any())
                return false;  // no valid sensors 

            if (validSensors.Any(x => x.Temperature > (maxTemperature + ExtendMaxTemperature)))
                return false;  // at least one of the temperature sensors value is valid and above maxTemperature.  

            onTemperature = validSensors.Max(x => x.Temperature);
            return true;
        }

        public bool MustTurnOff(int maxTemperature)
        {
            var validSensors = sensors.Where(x => x.TimeStamp > DateTime.UtcNow.AddSeconds(-2) && x.Temperature < 6000);
            if (!validSensors.Any())
                return true; // no valid sensors

            if (onTemperature < 10000 && validSensors.Max(x => x.Temperature) > onTemperature + 20)
                return true; // If hottest sensor is 50C higher than the temperature last time we turned on, then turn off. 

            return validSensors.Any(x => x.Temperature > (maxTemperature + ExtendMaxTemperature)); // turn off, if we reached maxTemperature. 
        }

        public string Name()
        {
            return sensors.First().Name;
        }

        public override string ToString()
        {
            string msg = string.Empty;
            foreach (var s in sensors)
                msg += s.Temperature.ToString("N0") + ", ";

            return $"{SwitchBoard}.{port} is {(LastOn > LastOff ? "ON" : "OFF")}, {msg} {onTemperature.ToString("N0")};   ";
        }
    }
}
