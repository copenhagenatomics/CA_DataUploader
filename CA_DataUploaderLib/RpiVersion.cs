using CA_DataUploaderLib.Extensions;
using CA_DataUploaderLib.Helpers;
using CA_DataUploaderLib.IOconf;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace CA_DataUploaderLib
{
    public class RpiVersion
    {
        private static readonly OperatingSystem _OS = Environment.OSVersion;

        public static string GetWelcomeMessage(string purpose, CALogLevel logLevel)
        {
            var sb = new StringBuilder();
            sb.AppendLine("This is Open Source code by Copenhagen Atomics");
            sb.AppendLine(purpose);
            sb.AppendLine();
            if (logLevel == CALogLevel.Debug)
            {
                sb.AppendLine("UTC time now: " + DateTime.UtcNow.ToString("ddd MMM dd. HH:mm:ss"));
                sb.AppendLine(GetSoftware());
                sb.AppendLine(GetHardware());
                sb.AppendLine();
            }
            sb.AppendLine("Press ESC to stop");
            sb.AppendLine();
            return sb.ToString();
        }

        public static string GetHardware()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Hardware:");
            sb.Append(GetHardwareInfo());
            sb.AppendLine("ChipName       : " + GetChipName());
            sb.AppendLine("CPU            : " + GetCPU());
            sb.AppendLine("Core count     : " + GetNumberOfCores().ToString());
            sb.AppendLine("Serial no      : " + GetSerialNumber());
            sb.AppendLine("WiFi     " + GetWiFi_SSID());
            sb.AppendLine();

            return sb.ToString();
        }

        public static string GetSoftware()
        {
            var hostAssembly = Assembly.GetEntryAssembly();
            var hostVersion = hostAssembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            Assembly asm = typeof(RpiVersion).Assembly;
            var copyright = asm.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright;
            var version = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            return 
$@"{hostAssembly?.GetName()}
    FileVersion {hostVersion}
{asm.GetName()}
    FileVersion {version}
    copyright {copyright}
    Kernel version {GetKernelVersion()}";
        }

        public static string GetHardwareInfo()
        {
            var dic = GetVersions();
            var key = GetHardwareKey();
            if(dic.ContainsKey(key))
                return dic[key];

            return "Unknown hardware";
        }

        private static Dictionary<string, string> GetVersions()
        {
            // load csv table from embedded resources
            // originate here:  http://elinux.org/RPi_HardwareHistory

            var assembly = Assembly.GetExecutingAssembly();
            using Stream stream = assembly.GetManifestResourceStream("CA_DataUploaderLib.RPi_versions.csv") 
                ?? throw new InvalidOperationException("failed to get rpi versions");
            using var reader = new StreamReader(stream);
            var lines = reader.ReadToEnd().SplitNewLine();
            var header = lines.First().Split(';').Select(x => x.PadRight(15) + ": ").ToArray();
            return lines.Skip(1).ToDictionary(x => GetKey(x), x => FormatString(header, x.Split(';')));
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
            if (OperatingSystem.IsWindows())
                return "PC";

            // https://elinux.org/RPi_HardwareHistory
            if (_OS.Platform == PlatformID.Unix)
                return DULutil.ExecuteShellCommand("cat /proc/cpuinfo | grep 'Revision' | awk '{print $3}' | sed 's/^1000//'").Trim();

            return RuntimeInformation.OSDescription;
        }

        private static string GetChipName()
        {
            if (_OS.Platform == PlatformID.Unix)
                return DULutil.ExecuteShellCommand("cat /proc/cpuinfo | grep 'Hardware' | awk '{print $3}' | sed 's/^1000//'").Trim();

            return Environment.Is64BitProcess ? "64 bit" : "32 bit";
        }

        private static string GetSerialNumber()
        {
            if (_OS.Platform == PlatformID.Unix)
                return DULutil.ExecuteShellCommand("cat /proc/cpuinfo | grep 'Serial' | awk '{print $3}' | sed 's/^1000//'").Trim();

            return "unknown";
        }

        private static string GetWiFi_SSID()
        {
            if (_OS.Platform == PlatformID.Unix)
                return DULutil.ExecuteShellCommand("iwgetid").Trim();

            return "unknown";
        }
        

        public static string GetFreeDisk()
        {
            if (_OS.Platform == PlatformID.Unix)
                return DULutil.ExecuteShellCommand("df -h").Trim();
            if (_OS.Platform == PlatformID.Win32NT)
            {
                WriteResourceToFile("df.bat", "df.bat");
                // return ExecuteShellCommand("df.bat").Trim();  Does not work -> need debugging. 
            }

            return string.Empty;
        }

        private static void WriteResourceToFile(string resourceName, string fileName)
        {
            using var resource = Assembly.GetCallingAssembly().GetManifestResourceStream("CA_DataUploaderLib." + resourceName)
                ?? throw new InvalidOperationException("failed to get resource CA_DataUploaderLib." + resourceName);
            using var file = new FileStream(fileName, FileMode.Create, FileAccess.Write);
            if (!File.Exists(fileName))
                resource.CopyTo(file);
        }

        private static string? GetCPU()
        {
            if (_OS.Platform == PlatformID.Unix)
                return DULutil.ExecuteShellCommand("cat /proc/cpuinfo | grep 'model name'").SplitNewLine().First().Substring(18).Trim();

            return Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE");
        }

        private static string GetKernelVersion()
        {
            if(_OS.Platform == PlatformID.Unix)
                return DULutil.ExecuteShellCommand("uname -r").Trim();

            return _OS.VersionString;
        }

        private static int GetNumberOfCores()
        {
            if(_OS.Platform == PlatformID.Unix)
                return DULutil.ExecuteShellCommand("cat /proc/cpuinfo | grep 'model name'").SplitNewLine().Count;

            return Environment.ProcessorCount;
        }

        public static bool IsWindows() => OperatingSystem.IsWindows();
    }
}
