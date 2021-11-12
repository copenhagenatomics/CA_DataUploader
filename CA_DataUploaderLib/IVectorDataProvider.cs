using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CA.LoopControlPluginBase;

namespace CA_DataUploaderLib
{
    public interface ISubsystemWithVectorData
    {
        string Title { get; }
        List<VectorDescriptionItem> GetVectorDescriptionItems();
        /// <returns>the vector description items for items related to local boards in the same order as <see cref="GetVectorDescriptionItems"/></returns>
        List<VectorDescriptionItem> GetLocalInputsDescriptionItems();
        /// <returns>The input values coming from local boards</returns>
        IEnumerable<SensorSample> GetInputValues();
        IEnumerable<SensorSample> GetDecisionOutputs(NewVectorReceivedArgs inputVectorReceivedArgs);
        Task Run(CancellationToken token);
    }
}
