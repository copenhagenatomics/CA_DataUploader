using System;
using System.Collections.Generic;
using System.Linq;

namespace CA_DataUploaderLib
{
    public class SensorSample
    {
        public SensorSample(int id, string name, HeaterElement heater) { ID = id; Name = name; Heater = heater; if(heater != null) heater.sensors.Add(this); }
        public int ID { get; private set; }   // temperature sensor ID

        public string Key;
        public DateTime TimeStamp { get; set; }
        public MCUBoard Board { get; set; }

        public HeaterElement Heater { get; set; }  // if the IO.conf specify a heating element, else it is null

        public string Name { get; private set; }

        public bool MaxSlope;

        public double Value { get; set; }
        public double Reference { get; set; }

        public int Hub { get { return int.Parse(Key.Split('.')[0]); } }
        public int Jack { get { return int.Parse(Key.Split('.')[1]); } }

        public override string ToString()
        {
            if (Value > 9000)
                return "NC";

            return $"{Value}";
        }
    }
}
