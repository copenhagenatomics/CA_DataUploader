using System;
using System.Collections.Generic;

namespace CA.LoopControlPluginBase
{
    public class VectorArgs
    {
        public VectorArgs(Dictionary<string, double> vector, List<string> userCommands)
        {
            Vector = vector;
            UserCommands = userCommands;
        }

        private Dictionary<string, double> Vector { get; }
        public List<string> UserCommands { get; }

        public double this[string sensorName]
        {
            get => TryGetValue(sensorName, out double val) ? val : throw new IndexOutOfRangeException("Failed to find sensor " + sensorName);
            set => Vector[sensorName] = value;
        }

        public bool TryGetValue(string sensorName, out double value) => Vector.TryGetValue(sensorName, out value);

        internal bool After(string state_waitsensorname, string timeoutNameInVector) => this["vectortime"] >= this[state_waitsensorname] + this[timeoutNameInVector];
        internal bool After(string state_argoncycle_waittime, double timeout) => this["vectortime"] >= this[state_argoncycle_waittime] + timeout;
        internal void ResetWaitTime(string state_waitsensorname) => this[state_waitsensorname] = this["vectortime"];
    }
}