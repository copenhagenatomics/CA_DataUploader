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
        /// <returns>Unlike <see cref="GetInputValues"/>, these values are not specific to a given node e.g.  information about operations only done by a current cluster leader and after a leader change it is the new leader that reports these values.</returns>
        IEnumerable<SensorSample> GetGlobalInputValues() => [];
        Task Run(CancellationToken token);
    }

    public record SubsystemDescriptionItems(List<(IOconfNode node, List<VectorDescriptionItem> items)> Inputs)
    {
        public IEnumerable<VectorDescriptionItem> GlobalInputs { get; init; } = new List<VectorDescriptionItem>(0);
        public IEnumerable<VectorDescriptionItem> GetNodeInputs(IOconfNode node) => Inputs.Where(n => n.node == node).SelectMany(n => n.items);
    }
}
