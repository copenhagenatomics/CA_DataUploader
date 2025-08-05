#nullable enable
using CA_DataUploaderLib;
using CA_DataUploaderLib.Extensions;
using CA_DataUploaderLib.IOconf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Channels;

namespace UnitTests
{
    internal class FullDecisionTestContext
    {
        private readonly VectorDescription desc;
        private readonly double[] vectorData;
        private readonly CommandHandler cmd;
        private readonly ChannelReader<string> logs;
        public DateTime InitialTime { get; }

        public FullDecisionTestContext(string config, Dictionary<string, double>? initialVectorSamples = null)
        {
            var loader = new IOconfLoader();
            Subsystems.RegisterIOConfAndThirdPartyBoardsProtocols(loader);
            var ioconf = new IOconfFile(loader, config.SplitNewLine());
            var logger = new ChannelLogger();
            cmd = new CommandHandler(ioconf, runCommandLoop: false, logger: logger);
            logs = logger.Log;
            Subsystems.AddSubsystemsTo(ioconf, cmd);
            var extDesc = cmd.GetExtendedVectorDescription();
            desc = extDesc.VectorDescription;
            InitialTime = new DateTime(2020, 06, 22, 2, 2, 2, 100);//starting time is irrelevant as long as time continues moving forward, just some random date here
            DataVector? vector = null;
            var inputs = extDesc.InputsPerNode
                .SelectMany(node => node.Item2)
                .Select(item => new SensorSample(item.Descriptor, initialVectorSamples?.TryGetValue(item.Descriptor, out var val) == true ? val : 0))
                .ToList();
            cmd.MakeDecision(inputs, InitialTime, ref vector, []);//initial round of decisions so that all state machines initialize first
            vectorData = vector.Data;
            InitialTime = InitialTime.AddMilliseconds(100); //since we just ran a cycle above, set the initial time for the next cycle to run
        }

        public string GetAllLogs()
        {
            var allLogs = new StringBuilder();
            while (logs.TryRead(out var log))
                allLogs.AppendLine(log);
            return allLogs.ToString();
        }

        public ref double Field(string field)
        {
            for (int i = 0; i < desc.Length; i++)
                if (desc._items[i].Descriptor == field) return ref vectorData[i];

            throw new ArgumentOutOfRangeException(nameof(field), $"field not found {field}");
        }

        public void MakeDecisions(string? @event = null, double secondsSinceStart = 0) => MakeDecisions(@event != null ? [@event] : [], secondsSinceStart);
        public void MakeDecisions(List<string> events, double secondsSinceStart = 0) => MakeDecisions(events, new DataVector(vectorData, InitialTime.AddSeconds(secondsSinceStart)));
        private void MakeDecisions(List<string> events, DataVector vector)
        {
            foreach (var e in events)
                cmd.Execute(e, true);

            var acceptedEvents = cmd.DequeueEvents() ?? [];
            cmd.MakeDecisionUsingInputsFromNewVector(vector, vector, acceptedEvents.Select(e => e.Data).ToList());
        }

        internal class ChannelLogger : ILog
        {
            private readonly Channel<string> log = Channel.CreateUnbounded<string>();
            public ChannelReader<string> Log => log.Reader;
            public void LogData(LogID id, string msg) => log.Writer.TryWrite($"data-{id}-{msg}");
            public void LogError(LogID id, string msg, Exception ex) => log.Writer.TryWrite($"error-{id}-{msg}-{ex}");
            public void LogError(LogID id, string msg) => log.Writer.TryWrite($"error-{id}-{msg}");
            public void LogInfo(LogID id, string msg, string? user = null) => log.Writer.TryWrite($"info-{id}-{msg}");
        }
    }
}
