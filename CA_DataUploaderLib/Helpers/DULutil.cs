﻿using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CA_DataUploaderLib.Helpers
{
    public class DULutil
    {
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
            using (var p = CreateAndStartShellProcess(command) ?? throw new InvalidOperationException($"Failed to start process {command}"))
            {
                string? err = null;
                p.ErrorDataReceived += (sender, e) => err += e.Data;
                string output = p.StandardOutput.ReadToEnd();
                if (!p.WaitForExit(waitForExit))
                    CALog.LogData(LogID.A, $"Timed out waiting for command to exit: {command}");
                else
                    p.WaitForExit(); // make sure all async events are finished handling https://docs.microsoft.com/en-us/dotnet/api/system.diagnostics.process.waitforexit?view=net-5.0#System_Diagnostics_Process_WaitForExit_System_Int32_
                if (!string.IsNullOrEmpty(err))
                    CALog.LogData(LogID.A, $"Error while running command {command} - {err}");

                p.ErrorDataReceived -= (sender, e) => err += e.Data;
                return output;
            }
        }

        public static Process? CreateAndStartShellProcess(string command)
        {
            return Process.Start(new ProcessStartInfo()
            {
                FileName = "/bin/bash",
                Arguments = "-c \"" + command + "\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                WorkingDirectory = Environment.CurrentDirectory
            });
        }
    }
}
