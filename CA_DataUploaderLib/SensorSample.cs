using CA_DataUploaderLib.IOconf;
using System;

namespace CA_DataUploaderLib
{
    public class SensorSample
    {
        public double Value;
        public DateTime TimeStamp;
        public IOconfInput Input = null;
        public IOconfMath Math = null;
        public string Other = null;
        public double ReadSensor_LoopTime;  // in miliseconds. 

        public SensorSample(IOconfInput input, double value = 0)
        {
            Value = value;
            TimeStamp = DateTime.UtcNow;
            Input = input;
        }

        public SensorSample(string other, double value = 0)
        {
            Value = value;
            TimeStamp = DateTime.UtcNow;
            Other = other;
        }

        public string Name { get { return (Input != null ? Input.Name : (Math != null ? Math.Name : Other)); } }
    }
}
