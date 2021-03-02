using System;
using System.Collections.Generic;
using System.Linq;

namespace CA_DataUploaderLib
{
    public class NewVectorReceivedArgs
    {
        public NewVectorReceivedArgs(List<SensorSample> vector) 
        {
            Vector = vector.AsReadOnly();
        }

        public IReadOnlyCollection<SensorSample> Vector { get; }
        public SensorSample this[string sensorName] => TryGetValue(sensorName) ?? throw new IndexOutOfRangeException("Failed to find sensor " + sensorName);
        public SensorSample TryGetValue(string sensorName) => Vector.SingleOrDefault(v => v.Name == sensorName);
    }
}