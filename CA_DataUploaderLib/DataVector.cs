using System;

namespace CA_DataUploaderLib
{
    public class DataVector
    {
        public readonly DateTime timestamp;
        public readonly double[] data;

        public DataVector(double[] data, DateTime time) { this.data = data; timestamp = time; }

        public byte[] buffer {
            get
            {
                var raw = new byte[data.Length * sizeof(double) + sizeof(long)];
                Buffer.BlockCopy(BitConverter.GetBytes(timestamp.Ticks), 0, raw, 0, 8);
                Buffer.BlockCopy(data, 0, raw, 8, raw.Length - sizeof(long));
                return raw;
            }
        }

        public int Count() => data.Length;
    }
}
