using System;
using System.Collections.Generic;
using System.Linq;

namespace CA_DataUploaderLib
{
    public class SensorSample
    {
        public SensorSample(int id, string key, string name, MCUBoard board) 
        { 
            ID = id;
            Board = board;
            Name = name;
            Key = key;
        }
        public int ID { get; private set; }   // temperature sensor ID

        public string Key;
        public int hubID;
        public DateTime TimeStamp { get; set; }
        public MCUBoard Board { get; set; }

        public string Name { get; private set; }

        public bool MaxSlope;

        public double Value { get; set; }
        public double Reference { get; set; }

        public int Hub { get { return hubID; } }

        public string NumberOfPorts { get; set; }
        public int Jack { get { return int.Parse(Key.Split('.')[1]); } }

        public override string ToString()
        {
            if (Value > 9000)
                return "NC";

            return $"{Value}";
        }
    }
}
