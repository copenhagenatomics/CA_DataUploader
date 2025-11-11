using System;

namespace CA.LoopControlPluginBase
{
    public ref struct DataVector
    {
        private readonly Span<double> _data;
        public DataVector(DateTime time, Span<double> data)
        {
            Time = time;
            _data = data;
        }

        public DateTime Time { get; }
        /// <summary>gets the vector data at the specified vector index</summary>
        public ref double this[int i] { get => ref _data[i]; }

        [Obsolete("Use TimeAfter(long) instead. This method will overflow for values > 596 hours.")]
        public double TimeAfter(int milliseconds) => Time.AddMilliseconds(milliseconds).ToOADate();
        public double TimeAfter(long milliseconds) => Time.AddMilliseconds(milliseconds).ToOADate();
        public bool Reached(double target) => Time >= DateTime.FromOADate(target);
    }
}