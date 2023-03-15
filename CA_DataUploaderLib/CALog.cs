#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CA_DataUploaderLib
{
    public enum LogID
    {
        A, B, C, D, E, F, G, H
    }

    public static class CALog
    {
        private static Dictionary<LogID, DateTime>? _nextSizeCheck;
        private static readonly string _logDir = Directory.GetCurrentDirectory();

        /// <remarks>any output before a custom logger is set is written to the console.</remarks>
        public static ISimpleLogger LoggerForUserOutput { get; set; } = new ConsoleLogger();
        public static int MaxLogSizeMB { get; set; } = 100;

        public static void LogData(LogID logID, string msg) => WriteToFile(logID, msg);
        public static void LogException(LogID logID, Exception ex)
        {
            var msg = ex.ToString();
            LoggerForUserOutput.LogError(ex);
            WriteToFile(logID, msg);
        }

        public static void LogInfoAndConsoleLn(LogID logID, string msg)
        {
            LoggerForUserOutput.LogInfo(msg);
            WriteToFile(logID, msg);
        }

        public static void LogErrorAndConsoleLn(LogID logID, string error)
        {
            LoggerForUserOutput.LogError(error);
            WriteToFile(logID, error);
        }

        public static void LogErrorAndConsoleLn(LogID logID, string error, Exception ex)
        {
            LoggerForUserOutput.LogError(error);
            WriteToFile(logID, error + Environment.NewLine + ex.ToString());
        }

        public static void LogError(LogID logID, string error, Exception ex) => WriteToFile(logID, error + Environment.NewLine + ex.ToString());
        private static void WriteToFile(LogID logID, string msg)
        {
            try
            {
                lock (_logDir)
                {
                    // allways add timestamp and a NewLine
                    msg = $"{DateTime.Now:MM.dd HH:mm:ss.fff} - {msg}{Environment.NewLine}";
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
                InitDictionary(ref _nextSizeCheck);

            var filepath = Path.Combine(_logDir, logID.ToString() + ".log");
            if (DateTime.Now > _nextSizeCheck[logID] && File.Exists(filepath))
            {
                if (new FileInfo(filepath).Length > MaxLogSizeMB * 1024 * 1024)
                    File.Delete(filepath);

                _nextSizeCheck[logID] = DateTime.Now.AddMinutes(1);
            }

            return filepath;
        }

        private static void InitDictionary([NotNull]ref Dictionary<LogID, DateTime>? dictionary)
        {
            if (dictionary != null) return;

            dictionary = new Dictionary<LogID, DateTime>
            {
                { LogID.A, DateTime.Now },
                { LogID.B, DateTime.Now },
                { LogID.C, DateTime.Now },
                { LogID.D, DateTime.Now },
                { LogID.E, DateTime.Now },
                { LogID.F, DateTime.Now },
                { LogID.G, DateTime.Now },
                { LogID.H, DateTime.Now }
            };
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

            public EventsLogger(CommandHandler handler) 
            {
                this.handler = handler;
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
                bool enabled = false;
                var lockObj = new object();
                ShowLocalConsoleOutputWhenEnabled();
                EnableOnLocalUserCommands(
                    TimeSpan.FromSeconds(5),
                    () => Console.WriteLine("enabling local output for 5 seconds"),
                    () => Console.WriteLine("disabled local output, check the event log in plots for further command(s) output"));

                void ShowLocalConsoleOutputWhenEnabled()
                {
                    var reader = cmd.GetReceivedVectorsReader();
                    _ = Task.Run(async () =>
                    {
                        await foreach (var vector in reader.ReadAllAsync())
                        {
                            lock(lockObj)
                                if (!enabled) continue;
                            foreach (var e in vector.Events)
                                WriteLogEventTo(e.EventType, e.Data);
                        }
                    });
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
                    var timer = new Timer(_ =>
                    {
                        lock (lockObj)
                            enabled = false;
                        disabledCallback();
                    });
                    cmd.UserCommandReceived += OnUserCommandEnableOutput;
                    cmd.StopToken.Register(() =>
                    {//since we are not be able to deliver events after stopping, keep console output enabled
                        cmd.UserCommandReceived -= OnUserCommandEnableOutput;
                        timer.Change(Timeout.Infinite, Timeout.Infinite);//disable the timer
                        lock (lockObj)
                            enabled = true;
                    });

                    void OnUserCommandEnableOutput(object? sender, EventFiredArgs _)
                    {
                        timer.Change(duration, Timeout.InfiniteTimeSpan); //triggers after duration (ignores a previous trigger if pending)
                        bool justEnabled;
                        lock (lockObj)
                        {
                            if (justEnabled = !enabled)
                                enabled = true;
                        }
                        if (justEnabled)
                            enabledCallback();
                    }
                }
            }
        }
    }
}
