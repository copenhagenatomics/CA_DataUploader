using CA_DataUploaderLib.Extensions;
using CA_DataUploaderLib.IOconf;
using System;
using System.Collections.Generic;
using System.Linq;

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

        private Queue<Tuple<double, DateTime>> _filterQueue = new Queue<Tuple<double, DateTime>>();

        public bool MaxSlope;

        private SensorSample _value;
        public SensorSample Value
        {
            get { return _value; }
            set { SetValue(value); }
        }

        public double TimeoutValue
        {
            // if last sample is older than filter length, then set timeout. 
            get { return (_value.TimeStamp < DateTime.UtcNow.AddSeconds(-Filter.filterLength)) ? 10009 : _value.Value; }   // 10009 means timedout
        }

        public override string ToString()
        {
            if (Value.Value > 9000)
                return "NC";

            return $"{Value}";
        }

        private void SetValue(SensorSample value)
        {
            lock (_filterQueue)
            {
                var removeBefore = DateTime.UtcNow.AddSeconds(-Filter.filterLength);
                _filterQueue.Enqueue(new Tuple<double, DateTime>(value.Value, value.TimeStamp));
                while (_filterQueue.First().Item2 < removeBefore)
                {
                    _filterQueue.Dequeue();
                }

                var valid = _filterQueue.Where(x => x.Item1 < 10000 && x.Item1 != 0);
                if (valid.Any())
                {
                    _value = new SensorSample(Filter.SourceNames.First()) { Value = value.Value, TimeStamp = value.TimeStamp };
                    switch (Filter.filterType)
                    {
                        case FilterType.Average:
                            _value.Value = valid.Average(x => x.Item1);
                            return;
                        case FilterType.Max:
                            _value.Value = valid.Max(x => x.Item1);
                            return;
                        case FilterType.Min:
                            _value.Value = valid.Min(x => x.Item1);
                            return;
                        case FilterType.Triangle:
                            _value.Value = valid.TriangleFilter(Filter.filterLength);
                            return;
                    }
                }
                
                _value = value;
            }
        }

        public string FilterToString()
        {
            lock (_filterQueue)
            {
                return string.Join(",", _filterQueue.Select(x => x.Item1.ToString("N2").PadLeft(9)));
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
