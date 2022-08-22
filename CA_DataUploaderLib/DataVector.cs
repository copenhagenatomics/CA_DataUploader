using System;

namespace CA_DataUploaderLib
{
    public class DataVector
    {
        public DataVector(double[] data, DateTime time) { this.Data = data; Timestamp = time; }

        public byte[] Buffer {
            get
            {
                var raw = new byte[Data.Length * sizeof(double) + sizeof(long)];
                System.Buffer.BlockCopy(BitConverter.GetBytes(Timestamp.Ticks), 0, raw, 0, 8);
                System.Buffer.BlockCopy(Data, 0, raw, 8, raw.Length - sizeof(long));
                return raw;
            }
        }

        public DateTime Timestamp { get; private set; }
        public double[] Data { get; }
        public int Count() => Data.Length;

        internal static void InitializeOrUpdateTime(ref DataVector vector, int length, DateTime vectorTime)
        {
            if (vector == null)
                vector = new DataVector(new double[length], vectorTime);
            else
                vector.Timestamp = vectorTime;
        }
    }
}
