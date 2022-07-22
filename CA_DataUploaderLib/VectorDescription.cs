using System;
using System.Runtime.Serialization;
using System.Collections.Generic;
using System.Linq;

namespace CA_DataUploaderLib
{
    [DataContract]
    public class VectorDescription
    {
        [DataMember]
        public List<VectorDescriptionItem> _items { get; private set; } // only public because we need to Serialize  -> please Do NOT access from outside this class
        [DataMember] 
        public string Hardware { get; private set; }
        [DataMember] 
        public string Software { get; private set; }
        [DataMember] 
        public string IOconf;
        public int Length { get { return _items.Count; } }
        private Dictionary<string, int> _toIndex = new Dictionary<string, int>();

        public VectorDescription() { }
        public VectorDescription(List<VectorDescriptionItem> items, string hardware, string software) 
        { 
            _items = items;  // shall not change after it has been assigned. See _toIndex
            Hardware = hardware; 
            Software = software;

            var duplicates = _items.GroupBy(x => x.Descriptor).Where(x => x.Count() > 1).Select(x => x.Key);
            if (duplicates.Any())
                throw new Exception("Duplicates in the VectorDescription: " + string.Join(", ", duplicates));

            for(int i = 0; i < duplicates.Count(); i++)
                _toIndex.Add(_items[i].Descriptor, i);  
        }

        public string GetVectorItemDescriptions() { return string.Join(Environment.NewLine, _items.Select(x => x.Descriptor)); }
        public bool HasItem(string descriptor) => _items.Any(i => i.Descriptor == descriptor);
        public int IndexOf(string descriptor) => _toIndex[descriptor];
        public bool CanUpdateValue(string name) => _items[_toIndex[name]].DirectionType != DataTypeEnum.Input;   // will throw an exception if the name is not known. 
        public override string ToString()
        {
            string msg = string.Empty;
            int i = 1;
            foreach (var item in _items)
                msg += $"{Environment.NewLine}{i++} {item.Descriptor}";

            return msg;
        }

    }

    [DataContract]
    public class VectorDescriptionItem
    {
        public VectorDescriptionItem() { }
        public VectorDescriptionItem(string datatype, string descriptor, DataTypeEnum direction) { Descriptor = descriptor; DataType = datatype; DirectionType = direction; }
        [DataMember] 
        public string Descriptor { get; private set; }  // Name of data line in webchart. 
        [DataMember] 
        public DataTypeEnum DirectionType { get; private set; }
        [DataMember] 
        public string DataType { get; private set; }

    }
}
