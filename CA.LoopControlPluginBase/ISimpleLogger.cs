using System;

namespace CA.LoopControlPluginBase
{
    public interface ISimpleLogger
    {
        void LogInfo(string message);
        void LogError(string message);
        void LogError(Exception ex);
        void LogData(string message);
    }
}