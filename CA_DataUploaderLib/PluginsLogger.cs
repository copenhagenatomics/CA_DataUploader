using System;
using CA.LoopControlPluginBase;

namespace CA_DataUploaderLib
{
    public class PluginsLogger : ISimpleLogger
    {
        private readonly string pluginName;

        public PluginsLogger(string pluginName)
        {
            this.pluginName = pluginName;
        }

        public void LogError(string message) => CALog.LogErrorAndConsoleLn(LogID.A, FormatMessage(message));
        public void LogError(Exception ex) => CALog.LogException(LogID.A, ex);
        public void LogInfo(string message) => CALog.LogInfoAndConsoleLn(LogID.A, FormatMessage(message));
        public void LogData(string message) => CALog.LogData(LogID.B, FormatMessage(message));
        private string FormatMessage(string message) => message.StartsWith(pluginName) ? message : $"{pluginName} {message}";
    }
}