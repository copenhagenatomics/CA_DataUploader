using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CA.LoopControlPluginBase;
using CA_DataUploaderLib.IOconf;

namespace CA_DataUploaderLib
{
    public interface ISubsystemWithVectorData
    {
        string Title { get; }
        SubsystemDescriptionItems GetVectorDescriptionItems();
        /// <returns>The input values coming from local boards</returns>
        IEnumerable<SensorSample> GetInputValues();
        IEnumerable<SensorSample> GetDecisionOutputs(NewVectorReceivedArgs inputVectorReceivedArgs);
        Task Run(CancellationToken token);
    }

    public record SubsystemDescriptionItems(List<(IOconfNode node, List<VectorDescriptionItem> items)> Inputs, List<VectorDescriptionItem> Outputs)
    {
        public IEnumerable<VectorDescriptionItem> GetNodeInputs(IOconfNode node) => Inputs.Where(n => n.node == node).SelectMany(n => n.items);
    }
}
