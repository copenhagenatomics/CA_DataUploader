using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace CA_DataUploaderLib
{
    public enum LogID
    {
        A, B, C, D, E, F, G, H
    }

    public static class CALog
    {
        private static DateTime _nextSizeCheck = DateTime.Now;
        private static string _logDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        public static int MaxLogSizeMB = 100;

        public static void LogData1(LogID logID, string msg)
        {
            File.AppendAllText(GetFilename(logID), msg);
        }

        public static void LogException(LogID logID, Exception ex)
        {
            File.AppendAllText(GetFilename(logID), ex.ToString() + Environment.NewLine);
        }

        public static void LogInfoAndConsole(LogID logID, string msg)
        {
            Console.Write(msg);
            File.AppendAllText(GetFilename(logID), msg);
        }

        public static void LogInfoAndConsoleLn(LogID logID, string msg)
        {
            Console.WriteLine(msg);
            File.AppendAllText(GetFilename(logID), msg + Environment.NewLine);
        }

        public static void LogColor(LogID logID, ConsoleColor color, string msg)
        {
            var temp = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.WriteLine(msg);
            Console.ForegroundColor = temp;
            File.AppendAllText(GetFilename(logID), msg);
        }

        public static void LogErrorAndConsole(LogID logID, string error)
        {
            var temp = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(error);
            Console.ForegroundColor = temp;
            File.AppendAllText(GetFilename(logID), error);
        }

        private static string GetFilename(LogID logID)
        {
            var filepath = Path.Combine(_logDir, logID.ToString() + ".log");
            if (DateTime.Now > _nextSizeCheck && File.Exists(filepath))
            {
                if (new FileInfo(filepath).Length > MaxLogSizeMB * 1024 * 1024)
                    File.Delete(filepath);

                _nextSizeCheck = DateTime.Now.AddMinutes(1);
            }

            return filepath;
        }
    }
}
