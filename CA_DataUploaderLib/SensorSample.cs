using CA_DataUploaderLib.IOconf;
using System;

namespace CA_DataUploaderLib
{
    public class SensorSample
    {
        public double Value;
        public DateTime TimeStamp;
        public IOconfInput Input = null;

        public SensorSample(IOconfInput input)
        {
            Value = 0;
            TimeStamp = DateTime.UtcNow;
            Input = input;
        }
    }
}
