#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace CA_DataUploaderLib
{
    public class DataVector
    {
        public DataVector(double[] data, DateTime time, IReadOnlyList<EventFiredArgs> events) => (this.Data, Timestamp, Events) = (data, time, events);

        /// <remarks>this does not include the events, which at the moment are reported separately by the uploader</remarks>
        public byte[] Buffer {
            get
            {
                var raw = new byte[Data.Length * sizeof(double) + sizeof(long)];
                System.Buffer.BlockCopy(BitConverter.GetBytes(Timestamp.Ticks), 0, raw, 0, 8);
                System.Buffer.BlockCopy(Data, 0, raw, 8, raw.Length - sizeof(long));
                return raw;
            }
        }

        public IReadOnlyList<EventFiredArgs> Events { get; }
        public DateTime Timestamp { get; private set; }
        public double[] Data { get; }
        public int Count => Data.Length;
        public double this[int index] => Data[index];

        internal static void InitializeOrUpdateTime([NotNull]ref DataVector? vector, int length, DateTime vectorTime)
        {
            if (vector == null)
                vector = new DataVector(new double[length], vectorTime);
            else
                vector.Timestamp = vectorTime;
        }
    }
}
