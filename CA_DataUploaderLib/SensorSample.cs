using CA_DataUploaderLib.IOconf;
using System;

namespace CA_DataUploaderLib
{
    public class SensorSample
    {
        public double Value;
        public DateTime TimeStamp;
        public IOconfInput Input = null;
        public double ReadSensor_LoopTime;  // in miliseconds. 

        public SensorSample(IOconfInput input)
        {
            Value = 0;
            TimeStamp = DateTime.UtcNow;
            Input = input;
        }
    }
}
