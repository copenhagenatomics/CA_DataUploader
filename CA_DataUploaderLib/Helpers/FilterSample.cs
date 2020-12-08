using CA_DataUploaderLib.Extensions;
using CA_DataUploaderLib.IOconf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CA_DataUploaderLib.Helpers
{
    public enum FilterType
    {
        None = 0,
        Average = 1, 
        Max = 2,
        Min = 3,
        SumAvg = 4,
        DiffAvg = 5,
        Triangle = 6,

    }

    public class FilterSample
    {
        public FilterSample(IOconfFilter filter)
        {
            Filter = filter;
            Output = new SensorSample("Filter_" + filter.Name);
        }

        public IOconfFilter Filter { get; private set; }

        private Queue<List<SensorSample>> _filterQueue = new Queue<List<SensorSample>>();


        public void Input(List<SensorSample> input)
        {
            CalculateOutput(input.Where(x => Filter.SourceNames.Contains(x.Name)).ToList());
        }

        public SensorSample Output { get; }

        public override string ToString()
        {
            if (Output.Value > 9000)
                return "NC";

            return $"{Output.Value}";
        }

        private void CalculateOutput(List<SensorSample> input)
        {
            lock (_filterQueue)
            {
                _filterQueue.Enqueue(input);
                var latestEntryTime = input.Select(y => y.TimeStamp.Ticks).AverageTime();
                var removeBefore = latestEntryTime.AddSeconds(-Filter.filterLength);

                while (_filterQueue.First().Any(x => x.TimeStamp < removeBefore))
                {
                    _filterQueue.Dequeue();
                }

                var validSamples = _filterQueue.Where(x => x.All(y => y.Value < 10000 && y.Value != 0)).ToList();
                if (validSamples.Any())
                {
                    var allSamples = validSamples.SelectMany(x => x.Select(y => y)).ToList();
                    Output.TimeStamp = validSamples.Last().Select(d => d.TimeStamp.Ticks).AverageTime();
                    Output.Value = Filter.filterType switch
                    {
                        FilterType.Average => allSamples.Average(x => x.Value),
                        FilterType.Max => allSamples.Max(x => x.Value),
                        FilterType.Min => allSamples.Min(x => x.Value),
                        FilterType.SumAvg => validSamples.Average(y => y.Sum(x => x.Value)),
                        FilterType.DiffAvg => validSamples.First().Count == 2 ? 
                            validSamples.Average(y => y[0].Value - y[1].Value) : 
                            throw new Exception("Filter DiffAvg must have two input source names"),
                        FilterType.Triangle => TriangleFilter(validSamples, Filter.filterLength, Output.TimeStamp),
                        _ => validSamples.Last().Select(x => x.Value).Average()
                    };
                    return;
                }

                // incase of no valid samples
                Output.Value = _filterQueue.Last().Select(x => x.Value).Average();
                Output.TimeStamp = _filterQueue.Last().Select(d => d.TimeStamp.Ticks).AverageTime();
            }
        }

        public string FilterToString()
        {
            lock (_filterQueue)
            {
                var sb = new StringBuilder();
                for(int i = 0;i<_filterQueue.First().Count;i++)
                    sb.AppendLine(string.Join(",", _filterQueue.Select(x => x[i].Value.ToString("N2").PadLeft(9))));

                return sb.ToString();
            }
        }

        public double GetFrequency()
        {
            lock (_filterQueue)
            {
                if (_filterQueue.Count < 2) 
                    return 0;

                return (_filterQueue.Count() - 1) / _filterQueue.Last().Select(d => d.TimeStamp.Ticks).AverageTime().Subtract(_filterQueue.First().Select(d => d.TimeStamp.Ticks).AverageTime()).TotalSeconds;
            }
        }

        public double FilterCount()
        {
            lock (_filterQueue)
            {
                return _filterQueue.Where(x => x.All(y => y.Value < 10000 && y.Value != 0)).Count();
            }
        }

        public bool HasSource(string sourceName) => Filter.SourceNames.Contains(sourceName);

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
