using System;
using System.Collections.Generic;
using System.Linq;

namespace CA_DataUploaderLib
{
    [Serializable]
    public class VectorDescription
    {
        public List<VectorDescriptionItem> _items;  // only public because we need to Serialize  -> please Do NOT access directly from outside
        public string Hardware;
        public string Software;
        public string IOconf;
        public int Length { get { return _items.Count; } }
        private Dictionary<string, int> _toIndex = new Dictionary<string, int>();

        public VectorDescription() { }
        public VectorDescription(List<VectorDescriptionItem> items, string hardware, string software) 
        { 
            _items = items; 
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

    [Serializable]
    public class VectorDescriptionItem
    {
        public VectorDescriptionItem() { }
        public VectorDescriptionItem(string datatype, string descriptor, DataTypeEnum direction) { Descriptor = descriptor; DataType = datatype; DirectionType = direction; }
        public string Descriptor { get; set; }  // Name of data line in webchart. 
        public DataTypeEnum DirectionType { get; set; }
        public string DataType { get; set; }

    }
}
