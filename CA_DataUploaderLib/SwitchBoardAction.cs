using System;
using System.Collections.Generic;
using CA_DataUploaderLib.Extensions;
using CA.LoopControlPluginBase;

namespace CA_DataUploaderLib
{
    public class SwitchboardAction
    {
        private static readonly DateTime ModifiedMaxDateTime = DateTime.FromOADate(DateTime.MaxValue.AddMilliseconds(-1).ToOADate()); 
        private readonly DateTime _timeToTurnOff;

        public bool IsOn { get; }
        public DateTime TimeToTurnOff => _timeToTurnOff >= ModifiedMaxDateTime ? DateTime.MaxValue : _timeToTurnOff; //we don't check max time exactly to avoid differences with the vector + allow the modified max date time.
        public SwitchboardAction(bool isOn, DateTime timeToTurnOff)
        {
            IsOn = isOn;
            _timeToTurnOff = timeToTurnOff;
        }

        ///<summary>gets a positive value of how many seconds are remaining (rounded up) or 0 or less for how many full seconds have passed since the time to turn off</summary>
        ///<remarks>if the action is to turn off and the time to turn off is in the future, this method will keep returning 0 until reaching the time to turn off</remarks>
        public int GetRemainingOnSeconds(DateTime currentVectorTime)
        {
            if (!IsOn && TimeToTurnOff > currentVectorTime) return 0;
            if (TimeToTurnOff == DateTime.MaxValue) return int.MaxValue;
            var remainingTime = TimeToTurnOff - currentVectorTime;
            return (int)Math.Ceiling(remainingTime.TotalSeconds); // switchboard ports can't be turn on less than 1 second.
        }

        /// <summary>returns a slightly changed version of the same command that can be used to request repeating the action</summary>
        /// <remarks>
        /// <see cref="TimeToTurnOff" /> will still return the max date even when it is changes (+1/-1 millisecond)
        /// <see cref="TimeToTurnOff" /> will return the vector time when the repeat off was requested or +1 millisecond for other on commands.
        /// The repeat actions use milliseconds instead of ticks modifications as the date stored in the vector only has millisecond precision.
        /// </remarks>
        public SwitchboardAction Repeat(DateTime currentVectorTime)
        {
            if (!IsOn)
                return new SwitchboardAction(false, currentVectorTime);
            if (_timeToTurnOff == ModifiedMaxDateTime)
                return new SwitchboardAction(true, DateTime.MaxValue); 
            if (_timeToTurnOff > ModifiedMaxDateTime) //we don't check max time exactly to avoid differences with the vector.
                return new SwitchboardAction(true, ModifiedMaxDateTime);
            return new SwitchboardAction(true, _timeToTurnOff.AddMilliseconds(1));
        }
        public IEnumerable<SensorSample> ToVectorSamples(string portName, DateTime currentVectorTime)
        {
            // we use GetRemainingOnSeconds instead of IsOn so that:
            // 1. the reported vector correctly states the expected on/off state
            // 2. the port still turns off even if the switchbox did not on its own
            // 3. the port can be turned off in sub second durations (up to vector decision frequency),
            //    which could happen when an actuation vector is missed or when resuming from a safe valve.
            yield return new SensorSample(GetOnName(portName), GetRemainingOnSeconds(currentVectorTime) > 0 ? 1.0 : 0.0);
            yield return new SensorSample(GetTimeOffName(portName), _timeToTurnOff.ToVectorDouble());
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
                IsOn == typedObj.IsOn && _timeToTurnOff == typedObj._timeToTurnOff :
                false;
        public override int GetHashCode() => HashCode.Combine(IsOn, _timeToTurnOff);
        private static string GetOnName(string portName) => portName + "_On/Off";
        private static string GetTimeOffName(string portName) => portName + "_timeOff";
    }
}
