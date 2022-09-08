using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CA_DataUploaderLib.IOconf;

namespace CA_DataUploaderLib
{
    public interface ISubsystemWithVectorData
    {
        string Title { get; }
        SubsystemDescriptionItems GetVectorDescriptionItems();
        /// <returns>The input values coming from local boards</returns>
        IEnumerable<SensorSample> GetInputValues();
        Task Run(CancellationToken token);
    }

    public record SubsystemDescriptionItems(List<(IOconfNode node, List<VectorDescriptionItem> items)> Inputs)
    {
        public IEnumerable<VectorDescriptionItem> GetNodeInputs(IOconfNode node) => Inputs.Where(n => n.node == node).SelectMany(n => n.items);
    }
}
