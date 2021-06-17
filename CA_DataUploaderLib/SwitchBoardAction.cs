using System;
using System.Collections.Generic;
using CA_DataUploaderLib.Extensions;
using CA.LoopControlPluginBase;

namespace CA_DataUploaderLib
{
    public class SwitchboardAction
    {
        public bool IsOn { get; }
        public DateTime TimeToTurnOff { get; }

        public SwitchboardAction(bool isOn, DateTime timeToTurnOff)
        {
            IsOn = isOn;
            TimeToTurnOff = timeToTurnOff;
        }

        public int GetRemainingOnSeconds(DateTime currentVectorTime)
        {
            if (!IsOn) return 0;
            var remainingTime = TimeToTurnOff - currentVectorTime;
            return (int) Math.Ceiling(remainingTime.TotalSeconds); // switchboard ports can't be turn on less than 1 second.
        }

        public IEnumerable<SensorSample> ToVectorSamples(string portName) 
        {
            yield return new SensorSample(GetOnName(portName), IsOn ? 1.0 : 0.0);
            yield return new SensorSample(GetTimeOffName(portName), TimeToTurnOff.ToVectorDouble());
        }

        public static IEnumerable<VectorDescriptionItem> GetVectorDescriptionItems(string portName) 
        {
            yield return new VectorDescriptionItem("double", GetOnName(portName), DataTypeEnum.Output);
            yield return new VectorDescriptionItem("double", GetTimeOffName(portName), DataTypeEnum.Output);
        }

        public static SwitchboardAction FromVectorSamples(NewVectorReceivedArgs args, string portName)
        {
            var isOnName = GetOnName(portName);
            var timeOffName = GetTimeOffName(portName);
            if (!args.TryGetValue(isOnName, out var isOn)) throw new ArgumentOutOfRangeException($"failed to find {isOnName}");
            if (!args.TryGetValue(timeOffName, out var timeOff)) throw new ArgumentOutOfRangeException($"failed to find {timeOffName}");
            return new SwitchboardAction(isOn == 1.0, timeOff.ToVectorDate());
        }

        public override bool Equals(object obj) => 
            obj != null && obj is SwitchboardAction typedObj ?
                IsOn == typedObj.IsOn && TimeToTurnOff == typedObj.TimeToTurnOff :
                false;
        public override int GetHashCode() => HashCode.Combine(IsOn, TimeToTurnOff);
        private static string GetOnName(string portName) => portName + "_On/Off";
        private static string GetTimeOffName(string portName) => portName + "_timeOff";
    }
}
