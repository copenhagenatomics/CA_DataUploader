#nullable enable
using System.Collections.Generic;

namespace CA_DataUploaderLib.IOconf
{
    public interface IIOconf
    {
        public void CheckConfig();
        public ConnectionInfo GetConnectionInfo();
        public string GetLoopName();
        public string GetLoopServer();
        public int GetVectorUploadDelay();
        public int GetMainLoopDelay();
        public CALogLevel GetOutputLevel();
        public IEnumerable<IOconfMap> GetMap();
        public IEnumerable<IOconfGeneric> GetGeneric();
        public IEnumerable<IOconfGenericOutput> GetGenericOutputs();
        public IEnumerable<IOconfTemp> GetTemp();
        public IOconfRPiTemp GetRPiTemp();
        public IEnumerable<IOconfHeater> GetHeater();
        public IEnumerable<IOconfOven> GetOven();
        public IEnumerable<IOconfAlert> GetAlerts();
        public IEnumerable<IOconfMath> GetMath();
        public IEnumerable<IOconfFilter> GetFilters();
        public IEnumerable<IOconfOutput> GetOutputs();
        public IEnumerable<IOconfState> GetStates();
        public IEnumerable<IOconfInput> GetInputs();
        public IEnumerable<T> GetEntries<T>();
        public string GetRawFile();
        public IEnumerable<string> GetBoardStateNames(string sensor);
    }
}
