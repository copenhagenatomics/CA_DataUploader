using System;
using System.Collections.Generic;

namespace CA.LoopControlPluginBase
{
    public abstract class LoopControlDecision
    { 
        public abstract string Name { get; }
        /// <summary>gets all fields defined by the plugin (called before <see cref="Initialize(VectorDescription)"/>)</summary>
        public abstract string[] PluginFields { get; }
        public abstract string[] HandledEvents { get; } //note these are just the names of the events handled, there is no accompanying help text.
        /// <summary>uses the field names in <see cref="VectorDescription"/> to get the field indexes that will be used in <see cref="MakeDecision(DataVector, List{string})"/></summary>
        /// <param name="desc"></param>
        public abstract void Initialize(VectorDescription desc);
        /// <summary>runs the decision logic based on the provided <see cref="DataVector"/>, updating it with new states and outputs</summary>
        /// <param name="vector">initially the vector contains the latest inputs, default values for outputs and the last known states, which gets updated by any decision running before this decision</param>
        /// <param name="events">all events received in this decision cycle (only user commands at the moment)</param>
        /// <remarks>
        /// note: the list of string events leads to string based checks which are not very efficient, 
        /// but moving this out of decision making would require to globally map all possible events so that we pass event ids instead.
        /// However, these strings are very flexible and simple, so we will keep these at the moment
        /// </remarks>
        public abstract void MakeDecision(DataVector vector, List<string> events);
    }

    public ref struct DataVector
    {
        private readonly Span<double> _data;
        public DataVector(DateTime time, Span<double> data)
        {
            Time = time;
            _data = data;
        }

        public DateTime Time { get; }
        /// <summary>gets the vector data at the specified vector index</summary>
        public ref double this[int i] { get => ref _data[i]; }

        public double TimeAfter(int milliseconds) => Time.AddMilliseconds(milliseconds).ToOADate();
        public bool Reached(double sa_t_0_timeEvent_0) => DateTime.FromOADate(sa_t_0_timeEvent_0) > Time;
    }

    public class VectorDescription
    {
        private string[] Fields { get; }
        /// <summary>gets the amount of fields in the vector</summary>
        public int Count => Fields.Length;
        /// <summary>gets the vector field at the specified vector index</summary>
        public string this[int i] { get => Fields[i]; set { Fields[i] = value; } }

        public VectorDescription(string[] fields)
        {
            Fields = fields;
        }
    }
}
