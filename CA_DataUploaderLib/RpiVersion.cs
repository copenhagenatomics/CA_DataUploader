using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Reflection;
using System.Text;

namespace CA_DataUploaderLib
{
    public class RpiVersion
    {
        private static OperatingSystem _OS = Environment.OSVersion;

        public static string GetWelcomeMessage(string purpose, string i2cConfig = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Code made by Thomas Jam Pedersen...");
            sb.AppendLine("UTC time now: " + DateTime.UtcNow.ToString("ddd MMM dd. HH:mm:ss"));
            sb.AppendLine(purpose);
            sb.AppendLine();
            sb.AppendLine(GetSoftware());
            sb.AppendLine(GetHardware(i2cConfig));
            sb.AppendLine();
            sb.AppendLine("Press ESC to stop");
            sb.AppendLine();
            return sb.ToString();
        }

        public static string GetHardware(string i2cConfig = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Hardware:");
            sb.Append(GetHardwareInfo());
            sb.AppendLine("ChipName       : " + GetChipName());
            sb.AppendLine("CPU            : " + GetCPU());
            sb.AppendLine("Core count     : " + GetNumberOfCores().ToString());
            sb.AppendLine("Serial no      : " + GetSerialNumber());
            sb.AppendLine();

            if (i2cConfig != null)
                sb.AppendLine(i2cConfig);

            return sb.ToString();
        }

        public static string GetSoftware()
        {
            return "Kernal version : " + GetKernalVersion();
        }

        public static string GetHardwareInfo()
        {
            var dic = GetVersions();
            var key = GetHardwareKey();
            if(dic.ContainsKey(key))
                return dic[key];

            return "Unknown hardware";
        }

        public static string[] GetUSBports()
        {
            if (_OS.Platform == PlatformID.Unix)
                return ExecuteShellCommand("ls -1a /dev/USB*").Split("\n".ToCharArray(), StringSplitOptions.RemoveEmptyEntries).Select(x => x.Replace("\r", "").Trim()).ToArray();

            return SerialPort.GetPortNames();
        }
                
        private static Dictionary<string, string> GetVersions()
        {
            // load csv table from embedded resources
            // originate here:  http://elinux.org/RPi_HardwareHistory

            var assembly = Assembly.GetExecutingAssembly();
            using (Stream stream = assembly.GetManifestResourceStream("CA_DataUploaderLib.RPi_versions.csv"))
            using (StreamReader reader = new StreamReader(stream))
            {
                var lines = reader.ReadToEnd().Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries).ToList();
                var header = lines.First().Split(';').Select(x => x.PadRight(15) + ": ").ToArray();
                return lines.Skip(1).ToDictionary(x => GetKey(x), x => FormatString(header, x.Split(';')));
            }
        }

        private static string FormatString(string[] header, string[] values)
        {
            var result = new StringBuilder();
            for(int i=0; i<6; i++)
            {
                var str = header[i] + values[i];
                result.AppendLine(str);
            }

            return result.ToString();
        }

        private static string GetKey(string input)
        {
            return input.Substring(0, input.IndexOf(';')).Trim();
        }

        private static string GetHardwareKey()
        {
            //if (Debugger.IsAttached)
            //    return "a02082";

            if (IsWindows())
                return "PC";

            if(_OS.Platform == PlatformID.Unix)
                return ExecuteShellCommand("sudo cat /proc/cpuinfo | grep 'Revision' | awk '{print $3}' | sed 's/^1000//'").Trim();

            return "unknown";
        }

        private static string GetChipName()
        {
            //if (Debugger.IsAttached)
            //    return "BCM2835";

            if (_OS.Platform == PlatformID.Unix)
                return ExecuteShellCommand("sudo cat /proc/cpuinfo | grep 'Hardware' | awk '{print $3}' | sed 's/^1000//'").Trim();

            return Environment.Is64BitProcess ? "64 bit" : "32 bit";
        }

        private static string GetSerialNumber()
        {
            if (_OS.Platform == PlatformID.Unix)
                return ExecuteShellCommand("sudo cat /proc/cpuinfo | grep 'Serial' | awk '{print $3}' | sed 's/^1000//'").Trim();

            return "unknown";
        }

        private static string GetCPU()
        {
            if (_OS.Platform == PlatformID.Unix)
                return ExecuteShellCommand("sudo cat /proc/cpuinfo | grep 'model name'").Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries).First().Substring(18).Trim();

            return System.Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE");
        }

        private static string GetKernalVersion()
        {
            if(_OS.Platform == PlatformID.Unix)
                return ExecuteShellCommand("uname -r").Trim();

            return _OS.VersionString;
        }

        private static int GetNumberOfCores()
        {
            if(_OS.Platform == PlatformID.Unix)
                return ExecuteShellCommand("sudo cat /proc/cpuinfo | grep 'model name'").Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries).Count();

            return Environment.ProcessorCount;
        }

        private static string ExecuteShellCommand(string command)
        {
            // execute shell command:
            var info = new ProcessStartInfo()
            {
                FileName = "/bin/bash",
                Arguments = "-c \"" + command + "\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            var p = Process.Start(info);
            string output = p.StandardOutput.ReadToEnd();
            string err = p.StandardError.ReadToEnd();
            Console.WriteLine(err);
            p.WaitForExit();
            return output;
        }

        public static bool IsWindows()
        {
            return _OS.Platform.ToString().StartsWith("Win");
        }
    }
}
