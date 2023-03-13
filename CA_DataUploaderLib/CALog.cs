using CA.LoopControlPluginBase;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace CA_DataUploaderLib
{
    public enum LogID
    {
        A, B, C, D, E, F, G, H
    }

    public static class CALog
    {
        private static Dictionary<LogID, DateTime> _nextSizeCheck;
        private static readonly string _logDir = Directory.GetCurrentDirectory();
        public static int MaxLogSizeMB = 100;
        /// <remarks>any output before a custom logger is set is written to the console.</remarks>
        public static ISimpleLogger LoggerForUserOutput { get; set; } = new ConsoleLogger();

        public static void LogData(LogID logID, string msg)
        {
            WriteToFile(logID, msg);            
        }

        public static void LogException(LogID logID, Exception ex)
        {
            var msg = $"{DateTime.Now:MM.dd HH:mm:ss} - {ex}{Environment.NewLine}";
            LoggerForUserOutput.LogError(msg);
            WriteToFile(logID, msg);
        }

        public static void LogInfoAndConsoleLn(LogID logID, string msg)
        {
            LoggerForUserOutput.LogInfo(msg);
            WriteToFile(logID, msg + Environment.NewLine);
        }

        public static void LogErrorAndConsoleLn(LogID logID, string error)
        {
            error = DateTime.UtcNow.ToString("MM.dd HH:mm:ss.fff - ") + error;
            LoggerForUserOutput.LogError(error);
            WriteToFile(logID, error + Environment.NewLine);
        }

        public static void LogErrorAndConsoleLn(LogID logID, string error, Exception ex)
        {
            error = DateTime.UtcNow.ToString("MM.dd HH:mm:ss.fff - ") + error;
            LoggerForUserOutput.LogError(error);
            WriteToFile(logID, error + Environment.NewLine + ex.ToString() + Environment.NewLine);
        }

        public static void LogError(LogID logID, string error, Exception ex)
        {
            error = DateTime.UtcNow.ToString("MM.dd HH:mm:ss.fff - ") + error;
            WriteToFile(logID, error + Environment.NewLine + ex.ToString() + Environment.NewLine);
        }

        private static void WriteToFile(LogID logID, string msg)
        {
            try
            {
                lock (_logDir)
                {
                    // allways add a NewLine
                    if (!msg.EndsWith(Environment.NewLine))
                        msg += Environment.NewLine;

                    var time = DateTime.UtcNow.ToString("MM.dd HH:mm:ss.fff - ");
                    if (!msg.StartsWith(time))
                        msg = time + msg;

                    // allways add timestamp. 
                    File.AppendAllText(GetFilename(logID), msg);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                Console.WriteLine(msg);
            }
        }

        private static string GetFilename(LogID logID)
        {
            if (_nextSizeCheck == null)
                InitDictionary();

            var filepath = Path.Combine(_logDir, logID.ToString() + ".log");
            if (DateTime.Now > _nextSizeCheck[logID] && File.Exists(filepath))
            {
                if (new FileInfo(filepath).Length > MaxLogSizeMB * 1024 * 1024)
                    File.Delete(filepath);

                _nextSizeCheck[logID] = DateTime.Now.AddMinutes(1);
            }

            return filepath;
        }

        private static void InitDictionary()
        {
            _nextSizeCheck = new Dictionary<LogID, DateTime>();
            _nextSizeCheck.Add(LogID.A, DateTime.Now);
            _nextSizeCheck.Add(LogID.B, DateTime.Now);
            _nextSizeCheck.Add(LogID.C, DateTime.Now);
            _nextSizeCheck.Add(LogID.D, DateTime.Now);
            _nextSizeCheck.Add(LogID.E, DateTime.Now);
            _nextSizeCheck.Add(LogID.F, DateTime.Now);
            _nextSizeCheck.Add(LogID.G, DateTime.Now);
            _nextSizeCheck.Add(LogID.H, DateTime.Now);
        }

        public class ConsoleLogger : ISimpleLogger
        {
            public void LogData(string message) => Console.WriteLine(message);
            public void LogError(string message) => WriteLineToConsole(message, ConsoleColor.Red);
            public void LogError(Exception ex) => WriteLineToConsole(ex.ToString(), ConsoleColor.Red);
            public void LogInfo(string message) => Console.WriteLine(message);

            private static void WriteLineToConsole(string line, ConsoleColor color)
            {
                lock (Console.Out)
                {
                    var temp = Console.ForegroundColor;
                    Console.ForegroundColor = color;
                    Console.WriteLine(line);
                    Console.ForegroundColor = temp;
                }
            }
        }

        public class EventsLogger : ISimpleLogger
        {
            private readonly CommandHandler handler;
            private readonly bool useLocalEventsForOutput;

            public EventsLogger(CommandHandler handler, bool useLocalEventsForOutput = false) 
            {
                this.handler = handler;
                this.useLocalEventsForOutput = useLocalEventsForOutput;
                EnableTempClusterOutputOnLocalActions(handler);
            }
            public void LogData(string message) => handler.FireCustomEvent(message, DateTime.UtcNow, (byte)EventType.Log);
            public void LogError(string message) => handler.FireCustomEvent(message, DateTime.UtcNow, (byte)EventType.LogError);
            public void LogError(Exception ex) => handler.FireCustomEvent(ex.ToString(), DateTime.UtcNow, (byte)EventType.LogError);
            public void LogInfo(string message) => handler.FireCustomEvent(message, DateTime.UtcNow, (byte)EventType.Log);

            /// <summary>prints remoteEvents to the console for 5 seconds when detecting local user commands<summary>
            void EnableTempClusterOutputOnLocalActions(CommandHandler cmd)
            {
                var logger = new ConsoleLogger();
                Action subscribeAction = useLocalEventsForOutput 
                    ? () => cmd.EventFired += OnLocalEventFired 
                    : () => cmd.NewVectorReceived += OnVectorReceivedFired;
                Action unsubscribeAction = useLocalEventsForOutput
                    ? () => cmd.EventFired -= OnLocalEventFired
                    : () => cmd.NewVectorReceived -= OnVectorReceivedFired;
                EnableOnLocalUserCommands(
                    TimeSpan.FromSeconds(5),
                    () => { Console.WriteLine("enabling local output for 5 seconds"); subscribeAction(); },
                    () => { Console.WriteLine("disabled local output, check the event log in plots for further command(s) output"); unsubscribeAction(); });

                void OnLocalEventFired(object _, EventFiredArgs e) => WriteLogEventTo(e.EventType, e.Data);
                void OnVectorReceivedFired(object _, NewVectorWithEventsReceivedArgs args)
                {
                    var vector = args.Vector;
                    if (vector.Events == null) return;
                    foreach (var e in vector.Events)
                        WriteLogEventTo(e.EventType, e.Data);
                }

                void WriteLogEventTo(byte type, string data)
                {
                    if (type == (byte)EventType.LogError)
                        logger.LogError(data);
                    else if (type == (byte)EventType.Log)
                        logger.LogInfo(data);
                }

                void EnableOnLocalUserCommands(TimeSpan duration, Action enabledCallback, Action disabledCallback)
                {
                    var enabled = false;
                    var lockObj = new object();
                    var timer = new Timer(_ =>
                    {
                        lock (lockObj)
                            enabled = false;
                        disabledCallback();
                    });
                    cmd.EventFired += (object sender, EventFiredArgs e) =>
                    {
                        if (e.EventType != (byte)EventType.Command) return; //ignore commands

                        timer.Change(duration, Timeout.InfiniteTimeSpan); //triggers after duration (ignores a previous trigger if pending)
                        bool justEnabled;
                        lock (lockObj)
                        {
                            if (justEnabled = !enabled)
                                enabled = true;
                        }
                        if (justEnabled)
                            enabledCallback();
                    };
                }
            }
        }
    }
}
