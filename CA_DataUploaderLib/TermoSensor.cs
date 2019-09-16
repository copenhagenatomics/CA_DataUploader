using System;
using System.Collections.Generic;
using System.Linq;

namespace CA_DataUploaderLib
{
    public class TermoSensor
    {
        public TermoSensor(int id, string name, HeaterElement heater) { ID = id; Name = name; Heater = heater; if(heater != null) heater.sensors.Add(this); }
        public int ID { get; private set; }   // temperature sensor ID

        public string Key;
        public DateTime TimeStamp { get; set; }
        public MCUBoard Board { get; set; }

        public HeaterElement Heater { get; set; }  // if the IO.conf specify a heating element, else it is null

        public string Name { get; private set; }

        public bool MaxSlope;

        private double _temperature;
        private double _junction;
        public double Temperature
        {
            get { return _temperature; }
            set { _temperature = value; }
        }

        public double Junction
        {
            get { return _junction; }
            set { _junction = value; }
        }

        public int Hub { get { return int.Parse(Key.Split('.')[0]); } }
        public int Jack { get { return int.Parse(Key.Split('.')[1]); } }

        public override string ToString()
        {
            if (Temperature > 9000)
                return "NC";

            return $"{Temperature}";
        }
    }
}
