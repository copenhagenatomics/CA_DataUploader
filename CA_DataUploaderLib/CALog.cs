#nullable enable
using CA_DataUploaderLib.IOconf;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CA_DataUploaderLib
{
    public enum LogID
    {
        A, B, C, D, E, F, G, H
    }

    public class CALog : ILog
    {
        private static Dictionary<LogID, DateTime>? _nextSizeCheck;
        private static readonly string _logDir = Directory.GetCurrentDirectory();
        internal static readonly ILog Default = new CALog();
        private static readonly ILog _localLogger = new LocalLogger();

        /// <remarks>any output before a custom logger is set is written to the console.</remarks>
        public static ISimpleLogger LoggerForUserOutput { get; set; } = new ConsoleLogger();
        public static int MaxLogSizeMB { get; set; } = 100;

        void ILog.LogData(LogID id, string msg) => LogData(id, msg);
        void ILog.LogError(LogID id, string msg, Exception ex) => LogErrorAndConsoleLn(id, msg, ex);
        void ILog.LogError(LogID id, string msg) => LogErrorAndConsoleLn(id, msg);
        void ILog.LogInfo(LogID id, string msg, string? user) => LogInfoAndConsoleLn(id, msg, user);
        public static void LogData(LogID logID, string msg) => _localLogger.LogData(logID, msg);

        public static void LogInfoAndConsoleLn(LogID logID, string msg, string? user = null)
        {
            LoggerForUserOutput.LogInfo(msg);
            _localLogger.LogInfo(logID, msg, user);
        }

        public static void LogErrorAndConsoleLn(LogID logID, string error)
        {
            LoggerForUserOutput.LogError(error);
            _localLogger.LogError(logID, error);
        }

        public static void LogErrorAndConsoleLn(LogID logID, string error, Exception ex)
        {
            LoggerForUserOutput.LogError(error);
            _localLogger.LogError(logID, error, ex);
        }

        public static void LogError(LogID logID, string error, Exception ex) => _localLogger.LogError(logID, error, ex);


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
            private readonly IDictionary<byte, string>? nodeIdToName;

            public EventsLogger(IIOconf ioconf, CommandHandler handler)
            {
                this.handler = handler;
                nodeIdToName = ioconf.GetEntries<IOconfNode>().ToDictionary(n => n.NodeIndex, n => n.Name);
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
                var enabled = 0;
                ShowLocalConsoleOutputWhenEnabled();
                EnableOnLocalUserCommands(
                    TimeSpan.FromSeconds(5),
                    () => Console.WriteLine("Enabling local output for 5 seconds"),
                    () => Console.WriteLine("Disabled local output, check the event log in plots for further command(s) output"));

                void ShowLocalConsoleOutputWhenEnabled()
                {
                    var reader = cmd.GetReceivedVectorsReader();
                    _ = Task.Run(async () =>
                    {
                        await foreach (var vector in reader.ReadAllAsync(cmd.StopToken))
                        {
                            if (Interlocked.CompareExchange(ref enabled, 0, 0) == 0)
                                continue;
                            foreach (var e in vector.Events)
                                WriteLogEventTo(e.NodeId, e.EventType, e.Data);
                        }
                    });
                }

                void WriteLogEventTo(byte nodeId, byte type, string data)
                {
                    if (type == (byte)EventType.LogError)
                        logger.LogError(NodeIdToName(nodeId) + data);
                    else if (type == (byte)EventType.Log)
                        logger.LogInfo(NodeIdToName(nodeId) + data);
                }

                string NodeIdToName(byte nodeId) => nodeId != byte.MaxValue ? $"[{nodeIdToName?[nodeId]}] " : "";

                void EnableOnLocalUserCommands(TimeSpan duration, Action enabledCallback, Action disabledCallback)
                {
                    var timer = new Timer(_ =>
                    {
                        if (Interlocked.CompareExchange(ref enabled, 0, 1) == 1)
                            disabledCallback();
                    });
                    cmd.UserCommandReceived += OnUserCommandEnableOutput;
                    cmd.StopToken.Register(() =>
                    {//since we are not be able to deliver events after stopping, keep console output enabled
                        cmd.UserCommandReceived -= OnUserCommandEnableOutput;
                        timer.Change(Timeout.Infinite, Timeout.Infinite);//disable the timer
                        Interlocked.CompareExchange(ref enabled, 0, 1);
                    });

                    void OnUserCommandEnableOutput(object? sender, EventFiredArgs _)
                    {
                        timer.Change(duration, Timeout.InfiniteTimeSpan); //triggers after duration (ignores a previous trigger if pending)
                        if (Interlocked.CompareExchange(ref enabled, 1, 0) == 0)
                            enabledCallback();
                    }
                }
            }
        }

        public class LocalLogger : ILog
        {
            public void LogData(LogID id, string msg) => WriteToFile(id, msg);

            public void LogError(LogID id, string msg, Exception ex) => WriteToFile(id, msg + Environment.NewLine + ex.ToString());

            public void LogError(LogID id, string msg) => WriteToFile(id, msg);

            public void LogInfo(LogID id, string msg, string? user = null) => WriteToFile(id, msg, user);

            private static void WriteToFile(LogID logID, string msg, string? user = null)
            {
                try
                {
                    lock (_logDir)
                    {
                        // always add timestamp and a NewLine
                        msg = $"{DateTime.UtcNow:MM.dd HH:mm:ss.fff} - {msg}{(!string.IsNullOrEmpty(user) ? $" [{user}]" : "")}{Environment.NewLine}";
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
                if (DateTime.UtcNow > _nextSizeCheck[logID] && File.Exists(filepath))
                {
                    if (new FileInfo(filepath).Length > MaxLogSizeMB * 1024 * 1024)
                        File.Delete(filepath);

                    _nextSizeCheck[logID] = DateTime.UtcNow.AddMinutes(1);
                }

                return filepath;
            }

            private static void InitDictionary([NotNull] ref Dictionary<LogID, DateTime>? dictionary)
            {
                if (dictionary != null) return;

                dictionary = new Dictionary<LogID, DateTime>
                {
                    { LogID.A, DateTime.UtcNow },
                    { LogID.B, DateTime.UtcNow },
                    { LogID.C, DateTime.UtcNow },
                    { LogID.D, DateTime.UtcNow },
                    { LogID.E, DateTime.UtcNow },
                    { LogID.F, DateTime.UtcNow },
                    { LogID.G, DateTime.UtcNow },
                    { LogID.H, DateTime.UtcNow }
                };
            }
        }
    }
}
