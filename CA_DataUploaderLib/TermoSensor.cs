using System;
using System.Collections.Generic;
using System.Linq;

namespace CA_DataUploaderLib
{
    public class TermoSensor
    {
        public TermoSensor(int id, string name) { ID = id; Name = name; }
        public int ID { get; private set; }   // temperature sensor ID

        public string Key;
        public DateTime TimeStamp { get; set; }

        public string Name { get; private set; }

        private Queue<bool> _active = new Queue<bool>();

        public bool MaxSlope;

        private double _temperature;
        public double Temperature
        {
            get { return _temperature; }
            set
            {
                while (_active.Count > 10) _active.Dequeue();
                _active.Enqueue(value < 1500);
                _temperature = value;
            }
        }

        public int Hub { get { return int.Parse(Key.Split('.')[0]); } }
        public int Jack { get { return int.Parse(Key.Split('.')[1]); } }
    }
}
