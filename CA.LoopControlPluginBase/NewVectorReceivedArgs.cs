using System;
using System.Collections.Generic;

namespace CA.LoopControlPluginBase
{
    public class NewVectorReceivedArgs
    {
        public NewVectorReceivedArgs(Dictionary<string, double> vector)
        {
            this.Vector = vector;
        }

        private Dictionary<string, double> Vector { get; }
        public double this[string sensorName] => TryGetValue(sensorName, out double val) ? val : throw new IndexOutOfRangeException("Failed to find sensor " + sensorName);
        public bool TryGetValue(string sensorName, out double value) => Vector.TryGetValue(sensorName, out value);
    }
}