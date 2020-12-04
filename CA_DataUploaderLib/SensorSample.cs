﻿using CA_DataUploaderLib.IOconf;
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

        public IOconfInput Input = null;
        public IOconfMath Math = null;
        public string Other = null;
        public string Name { get { return (Input != null ? Input.Name : (Math != null ? Math.Name : Other)); } }

        private DateTime _timeStamp;
        public DateTime TimeStamp 
        { 
            get { return _timeStamp; }
            set { ReadSensor_LoopTime = value.Subtract(_timeStamp).TotalMilliseconds; _timeStamp = value; }
        } 
        public double ReadSensor_LoopTime { get; private set; }  // in miliseconds. 

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
    }
}
