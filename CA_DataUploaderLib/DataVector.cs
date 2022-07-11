using System;
using System.Linq;
using System.Collections.Generic;

namespace CA_DataUploaderLib
{
    public class DataVector
    {
        public readonly DateTime timestamp;
        public readonly List<double> vector;
        public readonly VectorDescription _vectorDescription;

        public DataVector(List<double> input, DateTime time, VectorDescription vectorDescription) { vector = input; timestamp = time; _vectorDescription = vectorDescription; }

        public byte[] buffer {
            get
            {
                var raw = new byte[vector.Count() * sizeof(double) + sizeof(long)];
                Buffer.BlockCopy(BitConverter.GetBytes(timestamp.Ticks), 0, raw, 0, 8);
                Buffer.BlockCopy(vector.ToArray(), 0, raw, 8, raw.Length - sizeof(long));
                return raw;
            }
        }

        public int Count() { return vector == null?0:vector.Count(); }
    }
}
