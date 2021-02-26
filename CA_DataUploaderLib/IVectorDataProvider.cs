using System.Collections.Generic;

namespace CA_DataUploaderLib
{
    public interface ISubsystemWithVectorData
    {
        IEnumerable<SensorSample> GetValues();
        List<VectorDescriptionItem> GetVectorDescriptionItems();
        string Title { get; }
    }
}
