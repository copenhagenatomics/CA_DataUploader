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
        public FilterSample(IOconfInput input, TimeSpan filterLength, FilterType filterType, int hubID)
        {
            Input = input;
            FilterLength = filterLength;
            _filterType = filterType;
            HubID = hubID;
        }

        public TimeSpan FilterLength { get; private set; }
        public FilterType _filterType { get; private set; }
        public int HubID { get; private set; }  // used by AverageTemperature to draw GUI
        public IOconfInput Input { get; private set; }

        private Queue<Tuple<double, DateTime>> _filterQueue = new Queue<Tuple<double, DateTime>>();

        public string SerialNumber { get { return Input.Map.Board.serialNumber; } } // used by AverageTemperature to draw GUI
        public bool MaxSlope;
        public string NumberOfPorts;
        public DateTime TimeStamp = DateTime.UtcNow;
        public double ReadSensor_LoopTime;

        private double _value;
        public double Value
        {
            get { return _value; }
            set { SetValue(value); }
        }

        public double TimeoutValue
        {
            // if last sample is older than filter length, then set timeout. 
            get { return (TimeStamp < DateTime.UtcNow.Subtract(FilterLength)) ? 10009 : _value; }   // 10009 means timedout
        }

        
        public string Name { get { return Input.Name; } }
        public int PortNumber { get { return Input.PortNumber; } }

        public override string ToString()
        {
            if (Value > 9000)
                return "NC";

            return $"{Value}";
        }

        private void SetValue(double value)
        {
            ReadSensor_LoopTime = DateTime.UtcNow.Subtract(TimeStamp).TotalMilliseconds;
            TimeStamp = DateTime.UtcNow;
            lock (_filterQueue)
            {
                var removeBefore = DateTime.UtcNow.Subtract(FilterLength);
                _filterQueue.Enqueue(new Tuple<double, DateTime>(value, DateTime.UtcNow));
                while (_filterQueue.First().Item2 < removeBefore)
                {
                    _filterQueue.Dequeue();
                }

                var valid = _filterQueue.Where(x => x.Item1 < 10000 && x.Item1 != 0);
                if (valid.Any())
                {
                    switch (_filterType)
                    {
                        case FilterType.Average:
                            _value = valid.Average(x => x.Item1);
                            return;
                        case FilterType.Max:
                            _value = valid.Max(x => x.Item1);
                            return;
                        case FilterType.Min:
                            _value = valid.Min(x => x.Item1);
                            return;
                        case FilterType.Triangle:
                            _value = valid.Select(x => x.Item1).TriangleFilter();
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
            return Value != 0 && TimeoutValue < 10000;
        }
    }
}
