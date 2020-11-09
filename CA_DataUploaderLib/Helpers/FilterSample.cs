using CA_DataUploaderLib.Extensions;
using CA_DataUploaderLib.IOconf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace CA_DataUploaderLib.Helpers
{
    public enum FilterType
    {
        None = 0,
        Average = 1, 
        Max = 2,
        Min = 3,
        Triangle = 4
    }

    public class FilterSample
    {
        public FilterSample(IOconfFilter filter)
        {
            Filter = filter;
        }

        public IOconfFilter Filter { get; private set; }
        public List<int> VectorIndexs { get; private set; }

        private Queue<List<SensorSample>> _filterQueue = new Queue<List<SensorSample>>();

        public bool MaxSlope;

        public void Input(List<SensorSample> input)
        {
            var sourceNames = Filter.SourceNames.Select(x => x.Name);
            CalculateOutput(input.Where(x => sourceNames.Contains(x.Name)).ToList());
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
                var removeBefore = DateTime.UtcNow.AddSeconds(-Filter.filterLength);
                _filterQueue.Enqueue(input);
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
                        case FilterType.Triangle:
                            Output.Value = validSamples.TriangleFilter(Filter.filterLength);
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

                return (_filterQueue.Count() - 1) / _filterQueue.Last().Item2.Subtract(_filterQueue.First().Item2).TotalSeconds;
            }
        }

        public double FilterCount()
        {
            lock (_filterQueue)
            {
                return _filterQueue.Where(x => x.Item1 < 10000 && x.Item1 != 0).Count();
            }
        }


        public bool HasValidTemperature()
        {
            return Value.Value != 0 && TimeoutValue < 10000;
        }
    }
}
