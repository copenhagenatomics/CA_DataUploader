using System;

namespace CA_DataUploaderLib
{
    public interface ISimpleLogger
    {
        void LogInfo(string message, string? user = null);
        void LogError(string message);
        void LogError(Exception ex);
        void LogData(string message);
    }
}