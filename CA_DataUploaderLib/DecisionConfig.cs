using CA.LoopControlPluginBase;
using System.Collections.Generic;

namespace CA_DataUploaderLib
{
    public class DecisionConfig : IDecisionConfig
    {
        private readonly string decision;
        private readonly Dictionary<string, string> values;

        public DecisionConfig(string decision, Dictionary<string, string> values)
        {
            this.decision = decision;
            this.values = values;
        }

        public string Decision => decision;
        public IEnumerable<string> Fields => values.Keys;
        public bool TryGet(string fieldName, out string val) => values.TryGetValue(fieldName, out val);
    }
}
