using CA_DataUploaderLib.IOconf;
using System;

namespace CA_DataUploaderLib
{
    public class SensorSample
    {
        private double _value;
        public double Value { 
            get => _value; 
            set { TimeStamp = DateTime.UtcNow; _value = value; } 
        }

        public IOconfInput? Input { get; } = null;
        public string? Other { get; } = null;
        public string Name { get { return Input?.Name ?? Other ?? throw new InvalidOperationException("Failed to get the sensor name"); } }

        private DateTime _timeStamp;
        public DateTime TimeStamp 
        { 
            get => _timeStamp;
            set => _timeStamp = value;
        } 
        internal int InvalidReadsRemainingAttempts { get; set; } = 3000; //3k attempts = 5 (mins) x 60 (seconds) x 10 (cycles x second). The attempts are reset whenever we get valid values

        public SensorSample(IOconfInput input, double value = 0)
        {
            Value = value;
            Input = input;
        }

        public SensorSample(string other, double value = 0)
        {
            Value = value;
            Other = other;
        }

        public class InputBased : SensorSample
        {
            public InputBased(IOconfInput input, double value = 0) : base(input, value)
            {
            }

            public new IOconfInput Input => base.Input!;

            public bool HasSpecialDisconnectValue() => Input.IsSpecialDisconnectValue(Value);
            public InputBased Clone() => new(Input, Value);
        }
    }
}
