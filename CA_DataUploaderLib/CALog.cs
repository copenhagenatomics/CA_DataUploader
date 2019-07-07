using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace CA_DataUploaderLib
{
    public enum LogID
    {
        A, B, C, D, E, F, G, H
    }

    public static class CALog
    {
        private static Dictionary<LogID, DateTime> _nextSizeCheck;
        private static string _logDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        public static int MaxLogSizeMB = 100;
        private static bool _prependTimeStamp;

        public static void LogData1(LogID logID, string msg)
        {
            WriteToFile(logID, msg);            
        }

        public static void LogException(LogID logID, Exception ex)
        {
            var temp = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(ex.ToString() + Environment.NewLine);
            Console.ForegroundColor = temp;
            WriteToFile(logID, ex.ToString() + Environment.NewLine);
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

        public static void LogColor(LogID logID, ConsoleColor color, string msg)
        {
            var temp = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.WriteLine(msg);
            Console.ForegroundColor = temp;
            WriteToFile(logID, msg);
        }

        public static void LogErrorAndConsole(LogID logID, string error)
        {
            var temp = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(error);
            Console.ForegroundColor = temp;
            WriteToFile(logID, error);
        }

        private static void WriteToFile(LogID logID, string msg)
        {
            try
            {
                lock (_logDir)
                {
                    if(_prependTimeStamp)
                        File.AppendAllText(GetFilename(logID), DateTime.UtcNow.ToString("HH:mm:ss.fff - ") + msg);
                    else
                        File.AppendAllText(GetFilename(logID), msg);
                }

               _prependTimeStamp = msg.EndsWith(Environment.NewLine);
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
    }
}
