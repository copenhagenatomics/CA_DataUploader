using System;
using System.Collections.Generic;
using System.Linq;

namespace CA_DataUploaderLib
{
    [Serializable]
    public class VectorDescription
    {
        public List<VectorDescriptionItem> _items;  // only public because we need to Serialize  -> Do NOT add or remove anything from this list. 
        public string Hardware;
        public string Software;
        public string IOconf;
        public int Length { get { return _items.Count; } }

        public VectorDescription() { }
        public VectorDescription(List<VectorDescriptionItem> items, string hardware, string software) 
        { 
            _items = items; 
            Hardware = hardware; 
            Software = software;
        }

        public string GetVectorItemDescriptions() { return string.Join(Environment.NewLine, _items.Select(x => x.Descriptor)); }
        public bool HasItem(string descriptor) => _items.Any(i => i.Descriptor == descriptor);

        public int IndexOf(string descriptor) => _items.IndexOf(_items.Single(i => i.Descriptor == descriptor));

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
