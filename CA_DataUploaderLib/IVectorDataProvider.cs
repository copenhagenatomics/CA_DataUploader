using System;
using System.Collections.Generic;
using CA.LoopControlPluginBase;

namespace CA_DataUploaderLib
{
    public interface ISubsystemWithVectorData
    {
        string Title { get; }
        List<VectorDescriptionItem> GetVectorDescriptionItems();
        IEnumerable<SensorSample> GetInputValues();
        IEnumerable<SensorSample> GetDecisionOutputs(NewVectorReceivedArgs inputVectorReceivedArgs);
    }
}
