namespace CA.LoopControlPluginBase
{
    public class VectorDescription
    {
        private string[] Fields { get; }
        /// <summary>gets the amount of fields in the vector</summary>
        public int Count => Fields.Length;
        /// <summary>gets the vector field at the specified vector index</summary>
        public string this[int i] { get => Fields[i]; set { Fields[i] = value; } }

        public VectorDescription(string[] fields)
        {
            Fields = fields;
        }
    }
}
