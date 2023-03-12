#nullable enable
using System;
using System.Collections.Generic;

namespace CA_DataUploaderLib
{
    public class DataVector
    {
        public readonly DateTime timestamp;
        public readonly List<double> vector;

        public DataVector(List<double> input, DateTime time, IReadOnlyList<EventFiredArgs>? events)
        {
            vector = input; timestamp = time;
            Events = events;
        }

        /// <remarks>this does not include the events, which at the moment are reported separately by the uploader</remarks>
        public byte[] buffer {
            get
            {
                var raw = new byte[vector.Count * sizeof(double) + sizeof(long)];
                Buffer.BlockCopy(BitConverter.GetBytes(timestamp.Ticks), 0, raw, 0, 8);
                Buffer.BlockCopy(vector.ToArray(), 0, raw, 8, raw.Length - sizeof(long));
                return raw;
            }
        }

        public IReadOnlyList<EventFiredArgs>? Events { get; }
        public int Count => vector.Count;
    }
}
