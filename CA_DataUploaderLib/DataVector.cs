using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;

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
        public double? this[string name]
        {
            get
            {
                return (_vectorDescription.HasItem(name)) ? null: vector[_vectorDescription.IndexOf(name)];
            }
            set
            {
                var vdi = _vectorDescription._items.SingleOrDefault(x => x.Descriptor == name);
                if (vdi != null && vdi.DirectionType != DataTypeEnum.Input && value != null)
                    vector[_vectorDescription.IndexOf(name)] = value.Value;
                else
                    throw new InvalidDataException($"trying to save impossible value({value}) to DataVector[\"{name}\"]");
            }
        }

        public int Count() { return vector == null?0:vector.Count(); }
    }
}
