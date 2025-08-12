using System;

namespace CA_DataUploaderLib
{
    public interface ILog
    {
        void LogData(LogID id, string msg);
        void LogError(LogID id, string msg, Exception ex);
        void LogError(LogID id, string msg);
        void LogInfo(LogID id, string msg, string? user = null);
    }
}