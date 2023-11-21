#nullable enable
using CA.LoopControlPluginBase;
using CA_DataUploaderLib.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace UnitTests
{
    internal class DecisionTestContext
    {
        private readonly VectorDescription desc = new(Array.Empty<string>());
        private readonly double[] vectorData;
        private readonly List<LoopControlDecision> decisions = new();
        private readonly Dictionary<string, double>? initialVectorSamples;
        private readonly ILookup<string, (string command, Func<List<string>, bool> commandValidationFunction)> decisionCommandValidations;

        public DateTime InitialTime { get; }

        public DecisionTestContext(List<LoopControlDecision> decisions, Dictionary<string, double>? initialVectorSamples = null)
        {
            this.decisions = decisions;
            this.initialVectorSamples = initialVectorSamples;
            decisionCommandValidations = GetCommandValidations(decisions);
            var samples = (initialVectorSamples?.Select(kvp => (field: kvp.Key, value: kvp.Value)) ?? Enumerable.Empty<(string field, double value)>())
                .Concat(decisions.SelectMany(d => d.PluginFields.Select(f => (field: f.Name, value: 0d))))
                .ToArray();
            desc = new(samples.Select(s => s.field).ToArray());
            foreach (var decision in decisions)
                decision.Initialize(desc);
            vectorData = samples.Select(s => s.value).ToArray();
            InitialTime = new DateTime(2020, 06, 22, 2, 2, 2, 100);//starting time is irrelevant as long as time continues moving forward, just some random date here
            MakeDecisions(); //initial round of decisions so that all state machines initialize first
            InitialTime = InitialTime.AddMilliseconds(100); //since we just ran a cycle above, set the initial time for the next cycle to run
        }

        private DecisionTestContext(VectorDescription desc, double[] vectorData, DateTime initialTime, List<LoopControlDecision> decisions)
        {
            this.desc = desc;
            this.vectorData = vectorData;
            this.InitialTime = initialTime;
            this.decisions = decisions;
            decisionCommandValidations = GetCommandValidations(decisions);
        }

        public DecisionTestContext GetNewContextWithIndependentVectorData()
        {
            var data = new double[vectorData.Length];
            vectorData.CopyTo(data, 0);//we copy the vector data to avoid operating on the same data
            return new(desc, data, InitialTime, decisions);
        }

        public DecisionTestContext WithReplacedDecision(LoopControlDecision decision)
        {
            var index = decisions.FindIndex(d => d.Name == decision.Name);
            if (index == -1) throw new ArgumentException("failed to find decision to replace");
            decisions[index] = decision;
            return new DecisionTestContext(decisions, initialVectorSamples);
        }

        public ref double Field(string field)
        {
            for (int i = 0; i < desc.Count; i++)
                if (desc[i] == field) return ref vectorData[i];

            throw new ArgumentOutOfRangeException(nameof(field), $"field not found {field}");
        }

        public void MakeDecisions(string? @event = null, double secondsSinceStart = 0) =>
            MakeDecisions(@event != null ? new List<string> { @event } : new(), secondsSinceStart);
        public void MakeDecisions(List<string> events, double secondsSinceStart = 0) => 
            MakeDecisions(decisions, events, new DataVector(InitialTime.AddSeconds(secondsSinceStart), vectorData));
        private void MakeDecisions(List<LoopControlDecision> decisions, List<string> events, DataVector vector)
        {
            foreach (var e in events)
            {
                var splitCommand = e.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
                if (!decisionCommandValidations[splitCommand.First()].Any(validation => validation.commandValidationFunction(splitCommand)))
                    throw new ArgumentException($"rejected command/event: {e}");
            }

            foreach (var decision in decisions)
                decision.MakeDecision(vector, events);
        }
        private static ILookup<string, (string command, Func<List<string>, bool> commandValidationFunction)> GetCommandValidations(List<LoopControlDecision> decisions) =>
            decisions.SelectMany(DecisionExtensions.GetValidationCommands).ToLookup(validationCommand => validationCommand.command, StringComparer.OrdinalIgnoreCase);
    }
}
