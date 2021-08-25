using System;
using System.Collections.Generic;
using System.IO;

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

        public static void LogData(LogID logID, string msg)
        {
            WriteToFile(logID, msg);            
        }

        public static void LogException(LogID logID, Exception ex)
        {
            var msg = $"{DateTime.Now:MM.dd HH:mm:ss} - {ex}{Environment.NewLine}";
            WriteLineToConsole(msg, ConsoleColor.Red);
            WriteToFile(logID, msg);
        }

        public static void LogInfoAndConsole(LogID logID, string msg)
        {
            Console.Write(msg);
            WriteToFile(logID, msg);
        }

        public static void LogInfoAndConsoleLn(LogID logID, string msg)
        {
            Console.WriteLine(msg);
            WriteToFile(logID, msg + Environment.NewLine);
        }

        public static void LogInfoAndConsoleLn(LogID logID, string msg, Exception ex)
        {
            Console.WriteLine(msg);
            WriteToFile(logID, msg + Environment.NewLine + ex.ToString() + Environment.NewLine);
        }

        public static void LogColor(LogID logID, ConsoleColor color, string msg)
        {
            WriteLineToConsole(msg, color);
            WriteToFile(logID, msg);
        }

        public static void LogErrorAndConsoleLn(LogID logID, string error)
        {
            error = DateTime.UtcNow.ToString("MM.dd HH:mm:ss.fff - ") + error;
            WriteLineToConsole(error, ConsoleColor.Red);
            WriteToFile(logID, error + Environment.NewLine);
        }

        public static void LogErrorAndConsoleLn(LogID logID, string error, Exception ex)
        {
            error = DateTime.UtcNow.ToString("MM.dd HH:mm:ss.fff - ") + error;
            WriteLineToConsole(error, ConsoleColor.Red);
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
}
