using CA_DataUploaderLib.IOconf;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CA_DataUploaderLib
{
    public class SensorSample
    {
        public SensorSample(IOconfInput input) 
        {
            Input = input;
        }

        public int HubID;  // used by AverageTemperature to draw GUI
        public string SerialNumber;  // used by AverageTemperature to draw GUI
        public bool MaxSlope;

        public double Value { get; set; }
        public DateTime TimeStamp { get; set; }
        public IOconfInput Input { get; set; }

        public string Name { get { return Input.Name;  }  }
        public int PortNumber { get { return Input.PortNumber; } }
        public string NumberOfPorts { get; set; }

        public override string ToString()
        {
            if (Value > 9000)
                return "NC";

            return $"{Value}";
        }
    }
}
