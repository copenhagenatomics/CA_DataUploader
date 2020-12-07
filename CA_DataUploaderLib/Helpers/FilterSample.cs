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
        public List<int> VectorIndexs { get; private set; }

        private Queue<List<SensorSample>> _filterQueue = new Queue<List<SensorSample>>();

        public bool MaxSlope;

        public void Input(List<SensorSample> input)
        {
            CalculateOutput(input.Where(x => Filter.SourceNames.Contains(x.Name)).ToList());
        }

        public SensorSample Output { get; }

        // currently this only returns the first input value. 
        public double TimeoutValue 
        {
            // if last sample is older than filter length, then set timeout. 
            get { return Output.TimeStamp < DateTime.UtcNow.AddSeconds(-Filter.filterLength) ? 10009 : Output.Value; }   // 10009 means timedout
        }

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
                    switch (Filter.filterType)
                    {
                        case FilterType.Average:
                            Output.Value = allSamples.Average(x => x.Value);
                            Output.TimeStamp = allSamples.Select(d => d.TimeStamp.Ticks).AverageTime();
                            return;
                        case FilterType.Max:
                            Output.Value = allSamples.Max(x => x.Value);
                            Output.TimeStamp = validSamples.Last().Select(d => d.TimeStamp.Ticks).AverageTime(); 
                            return;
                        case FilterType.Min:
                            Output.Value = allSamples.Min(x => x.Value);
                            Output.TimeStamp = validSamples.Last().Select(d => d.TimeStamp.Ticks).AverageTime();
                            return;
                        case FilterType.SumAvg:
                            Output.Value = validSamples.Average(y => y.Sum(x => x.Value));
                            Output.TimeStamp = validSamples.Last().Select(d => d.TimeStamp.Ticks).AverageTime();
                            return;
                        case FilterType.DiffAvg:
                            if (validSamples.First().Count != 2)
                                throw new Exception("Filter DiffAvg must have two input source names");

                            Output.Value = validSamples.Average(y => y[0].Value - y[1].Value);
                            Output.TimeStamp = validSamples.Last().Select(d => d.TimeStamp.Ticks).AverageTime();
                            return;
                        case FilterType.Triangle:
                            Output.Value = validSamples.TriangleFilter(Filter.filterLength, latestEntryTime);
                            Output.TimeStamp = validSamples.Last().Select(d => d.TimeStamp.Ticks).AverageTime();
                            return;
                    }
                }

                // incase of no valid samples or invalid filter type
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


        public bool HasValidTemperature()
        {
            return Output.Value != 0 && TimeoutValue < 10000;
        }
    }
}
