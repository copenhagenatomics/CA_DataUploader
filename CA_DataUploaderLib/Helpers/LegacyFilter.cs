using CA_DataUploaderLib.Extensions;
using CA_DataUploaderLib.IOconf;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CA_DataUploaderLib.Helpers
{
    public class LegacyFilter(string name, FilterType filterType, double length, List<string> sourceNames, bool hideSource)
    {
        private readonly Queue<List<SensorSample>> _filterQueue = new();

        public void Input(List<SensorSample> inputs) => CalculateOutput(inputs.Where(x => SourceNames.Contains(x.Name)).ToList());
        public SensorSample Output { get; } = new SensorSample(name);
        public string Name { get; } = name;
        public List<string> SourceNames { get; } = sourceNames;
        public bool HideSource { get; } = hideSource;

        private void CalculateOutput(List<SensorSample> input)
        {
            lock (_filterQueue)
            {
                _filterQueue.Enqueue(input);
                var latestEntryTime = input.Select(y => y.TimeStamp.Ticks).AverageTime();
                var removeBefore = latestEntryTime.AddSeconds(-length);

                while (_filterQueue.First().Any(x => x.TimeStamp < removeBefore))
                {
                    _filterQueue.Dequeue();
                }

                var validSamples = _filterQueue.Where(x => x.All(y => y.Value < 10000 && y.Value != 0)).ToList();
                if (validSamples.Count > 0)
                {
                    var allSamples = validSamples.SelectMany(x => x.Select(y => y)).ToList();
                    Output.TimeStamp = validSamples.Last().Select(d => d.TimeStamp.Ticks).AverageTime();
                    Output.Value = filterType switch
                    {
                        FilterType.Average => allSamples.Average(x => x.Value),
                        FilterType.Max => allSamples.Max(x => x.Value),
                        FilterType.Min => allSamples.Min(x => x.Value),
                        FilterType.SumAvg => validSamples.Average(y => y.Sum(x => x.Value)),
                        FilterType.DiffAvg => validSamples.First().Count == 2 ? 
                            validSamples.Average(y => y[0].Value - y[1].Value) : 
                            throw new Exception("Filter DiffAvg must have two input source names"),
                        FilterType.Triangle => TriangleFilter(validSamples, length, Output.TimeStamp),
                        FilterType.None => validSamples.Last().Select(x => x.Value).Average(),
                        _ => throw new InvalidOperationException($"unexpected filter type in FilterSample: {filterType}")
                    };
                    return;
                }

                // incase of no valid samples
                Output.Value = _filterQueue.Last().Select(x => x.Value).Average();
                Output.TimeStamp = _filterQueue.Last().Select(d => d.TimeStamp.Ticks).AverageTime();
            }
        }

        public bool HasSource(string sourceName) => SourceNames.Contains(sourceName);

        /// <summary>
        /// filter where values weight more the closest they are to the latest. 
        /// </summary>
        private static double TriangleFilter(List<List<SensorSample>> list, double filterLength, DateTime latestEntryTime)  // filterLength in seconds
        {
            // find the sum of all timespans.  
            var sum = list.Sum(x => filterLength - latestEntryTime.Subtract(x.Select(y => y.TimeStamp.Ticks).AverageTime()).TotalSeconds);
            return list.Sum(x => x.Average(y => y.Value) * (filterLength - latestEntryTime.Subtract(x.Select(y => y.TimeStamp.Ticks).AverageTime()).TotalSeconds) / sum);
        }
    }
}
