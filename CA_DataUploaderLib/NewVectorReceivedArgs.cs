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
        public SensorSample this[string sensorName] => Vector.Single(v => v.Name == sensorName);
    }
}