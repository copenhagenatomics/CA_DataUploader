#nullable enable
using CA_DataUploaderLib.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CA_DataUploaderLib
{
    public class NewVectorWithEventsReceivedArgs : CA.LoopControlPluginBase.NewVectorReceivedArgs
    {
        public NewVectorWithEventsReceivedArgs(Dictionary<string, double> vector, DataVector dataVector) : base(vector) 
            => Vector = dataVector;

        public DataVector Vector { get; }
        public static NewVectorWithEventsReceivedArgs From(IReadOnlyList<SensorSample> vector, DateTime vectorTime, List<EventFiredArgs>? events) => 
            new(
                vector.WithVectorTime(vectorTime).ToDictionary(v => v.Name, v => v.Value), 
                new(vector.Select(v => v.Value).ToList(), vectorTime, events));
    }
}
