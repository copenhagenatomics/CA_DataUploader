using System;

namespace CA_DataUploaderLib
{
    /// <summary>
    /// Helps to log messages at a low frequency (max 2 messages every 5 minutes).
    /// </summary>
    /// <param name="time">Time provider</param>
    /// <param name="logType">Type/category of messages</param>
    public class LowFrequencyLog(TimeProvider time, string logType)
    {
        private long lastLogTime;
        private int logSkipped;

        public void Log<T>(Action<T, string> logAction, T args) => Log(logAction, args, ref lastLogTime, ref logSkipped);

        private void Log<T>(Action<T, string> logAction, T args, ref long lastLogTime, ref int logSkipped)
        {
            if (lastLogTime != 0 && time.GetElapsedTime(lastLogTime).TotalMinutes < 5)
            {
                if (logSkipped++ == 0)
                    logAction(args, $"{Environment.NewLine}Skipping further {logType} messages (max 2 every 5 minutes)");
                return;
            }

            lastLogTime = time.GetTimestamp();
            logAction(args, "");
            logSkipped = 0;
        }
    }
}
