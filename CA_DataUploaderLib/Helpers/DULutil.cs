using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace CA_DataUploaderLib.Helpers
{
    public class DULutil
    {
        private static readonly object SingleProcessLock = new object();
        private static readonly Stopwatch timeSinceLastProcessExit = new Stopwatch();
        public static void OpenUrl(string url)
        {
            try
            {
                Process.Start(url);
            }
            catch
            {
                // hack because of this: https://github.com/dotnet/corefx/issues/10361
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    url = url.Replace("&", "^&");
                    Process.Start(new ProcessStartInfo("cmd", $"/c start {url}") { CreateNoWindow = true });
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    Process.Start("xdg-open", url);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    Process.Start("open", url);
                }
                else
                {
                    throw;
                }
            }
        }

        public static string ExecuteShellCommand(string command, int waitForExit = 1000)
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

            lock (SingleProcessLock)
            {
                if (timeSinceLastProcessExit.IsRunning && timeSinceLastProcessExit.ElapsedMilliseconds < 10)
                    Thread.Sleep(20);

                using (var p = Process.Start(info))
                {
                    string err = null;
                    p.ErrorDataReceived += (sender, e) => err += e.Data;
                    string output = p.StandardOutput.ReadToEnd();
                    if (!p.WaitForExit(waitForExit))
                        CALog.LogData(LogID.A, $"timed out waiting for command to exit: {command}");
                    else
                        p.WaitForExit(); // make sure all async events are finished handling https://docs.microsoft.com/en-us/dotnet/api/system.diagnostics.process.waitforexit?view=net-5.0#System_Diagnostics_Process_WaitForExit_System_Int32_
                    if (!string.IsNullOrEmpty(err))
                        CALog.LogData(LogID.A, $"error while running command {command} - {err}");

                    p.ErrorDataReceived -= (sender, e) => err += e.Data;
                    timeSinceLastProcessExit.Restart();
                    return output;
                }
            }
        }
    }
}
