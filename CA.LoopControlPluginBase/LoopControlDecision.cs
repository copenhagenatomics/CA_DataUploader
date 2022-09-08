#nullable enable
using System.Collections.Generic;

namespace CA.LoopControlPluginBase
{
    public abstract class LoopControlDecision
    { 
        public abstract string Name { get; }
        /// <summary>gets all fields defined by the plugin (called before <see cref="Initialize(VectorDescription)"/>)</summary>
        public abstract PluginField[] PluginFields { get; }
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
    public enum FieldType { Input = 1, State = 2, Output = 3 }
    public record PluginField(string Name, FieldType Type)
    {
        public static implicit operator PluginField(string name) => new(name, FieldType.State);
        public static implicit operator PluginField((string name, FieldType type) tuple) => new(tuple.name, tuple.type);
    }
}
