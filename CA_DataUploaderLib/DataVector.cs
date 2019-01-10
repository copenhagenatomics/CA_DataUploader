using System;
using System.Linq;
using System.Collections.Generic;

namespace CA_DataUploaderLib
{
    public class DataVector
    {
        public DateTime timestamp;
        public List<double> vector;

        public byte[] buffer {
            get
            {
                var raw = new byte[vector.Count() * sizeof(double) + sizeof(long)];
                Buffer.BlockCopy(BitConverter.GetBytes(timestamp.Ticks), 0, raw, 0, 8);
                Buffer.BlockCopy(vector.ToArray(), 0, raw, 8, raw.Length - sizeof(long));
                return raw;
            }
        }
    }
}
