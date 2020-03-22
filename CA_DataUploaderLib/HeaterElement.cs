using CA_DataUploaderLib.IOconf;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CA_DataUploaderLib
{
    public class HeaterElement
    {
        private int OvenTargetTemperature;

        public IOconfHeater ioconf;
        private List<SensorSample> sensors = new List<SensorSample>();
        private SensorSample MaxSensor;
        public DateTime LastOn = DateTime.UtcNow.AddSeconds(-20); // assume nothing happened in the last 20 seconds
        public DateTime LastOff = DateTime.UtcNow.AddSeconds(-20); // assume nothing happened in the last 20 seconds
        private double onTemperature = 10000;
        public bool IsOn;
        public bool ManualMode;
        public double Current;  // Amps per element. 
        public bool IsActive { get { return OvenTargetTemperature > 0;  } }

        public HeaterElement(IOconfOven oven, SensorSample sample)
        {
            ioconf = oven.HeatingElement;
            MaxSensor = sample;
            if (!oven.OvenAreaMax)
                sensors.Add(sample);
        }

        public void SetTemperature(int value)
        {
            OvenTargetTemperature = Math.Min(value, ioconf.MaxTemperature);
        }

        public bool CanTurnOn()
        {
            if (ManualMode)
                return false;

            if (LastOff > DateTime.UtcNow.AddSeconds(-10))
                return false;  // has been turned off for less than 10 seconds. 

            var twoSecAgo = DateTime.UtcNow.AddSeconds(-2);
            var validSensors = sensors.Where(x => x.TimeStamp > twoSecAgo && x.Value < 6000);
            if (!validSensors.Any())
                return false;  // no valid sensors 

            if (validSensors.Any(x => x.Value > OvenTargetTemperature))
                return false;  // at least one of the temperature sensors value is valid and above maxTemperature.  

            if (MaxSensor.TimeStamp < twoSecAgo || MaxSensor.Value > 6000)
                return false;

            if (ioconf.MaxTemperature < MaxSensor.Value)
                return false; // element must never reach higher than set max temperautre. 

            onTemperature = validSensors.Max(x => x.Value);
            return true;
        }

        public bool MustTurnOff()
        {
            var twoSecAgo = DateTime.UtcNow.AddSeconds(-2);
            var validSensors = sensors.Where(x => x.TimeStamp > twoSecAgo && x.Value < 6000);
            if (!validSensors.Any())
                return true; // no valid sensors

            if (MaxSensor.TimeStamp < twoSecAgo || MaxSensor.Value > 6000)
                return true;

            if (MaxSensor.Value > ioconf.MaxTemperature)
                return true; // element must never reach higher than set max temperautre. 

            if (OvenTargetTemperature == 0  && ManualMode)
                return false;

            if (onTemperature < 10000 && validSensors.Max(x => x.Value) > onTemperature + 20)
                return true; // If hottest sensor is 20C higher than the temperature last time we turned on, then turn off. 

            return validSensors.Any(x => x.Value > OvenTargetTemperature); // turn off, if we reached maxTemperature. 
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

        public bool HasSensor(IEnumerable<SensorSample> allValidTemperatures)
        {
            return allValidTemperatures.Any(x => sensors.Any(y => y.Name == x.Name));
        }

        public bool TryAdd(SensorSample sensor, bool maxSensor)
        {
            if (sensors.Contains(sensor))
                return false;

            if(maxSensor)
                MaxSensor = sensor;
            else
                sensors.Add(sensor);

            return true;
        }
    }
}
