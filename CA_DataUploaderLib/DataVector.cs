using System;
using System.Linq;
using System.Collections.Generic;

namespace CA_DataUploaderLib
{
    public class DataVector
    {
        public readonly DateTime timestamp;
        public readonly List<double> vector;
        private readonly VectorDescription _vectorDescription;

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

        // DataVector["HeaterTop1_on_off"] = 1;
        // currentTemperature = DataVector["HeaterTop1_degc"];
        public double? this[string index]
        {
            get
            {
                return (_vectorDescription.HasItem(index)) ? null: vector[_vectorDescription.IndexOf(index)];
            }
            set
            {
                var vdi = _vectorDescription._items.SingleOrDefault(x => x.Descriptor == index);
                if(vdi != null && vdi.DirectionType != DataTypeEnum.Input && value != null) 
                    vector[_vectorDescription.IndexOf(index)] = value.Value;
            }
        }

        public int Count() { return vector == null?0:vector.Count(); }
    }
}
