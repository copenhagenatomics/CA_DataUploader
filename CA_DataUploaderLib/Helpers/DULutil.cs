using System;
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
                p.ErrorDataReceived += ErrorDataReceived;
                string output = p.StandardOutput.ReadToEnd();
                if (!p.WaitForExit(waitForExit))
                    CALog.LogData(LogID.A, $"Timed out waiting for command to exit: {command}");
                else
                    p.WaitForExit(); // make sure all async events are finished handling https://docs.microsoft.com/en-us/dotnet/api/system.diagnostics.process.waitforexit?view=net-5.0#System_Diagnostics_Process_WaitForExit_System_Int32_
                if (!string.IsNullOrEmpty(err))
                    CALog.LogData(LogID.A, $"Error while running command {command} - {err}");

                p.ErrorDataReceived -= ErrorDataReceived;
                return output;

                void ErrorDataReceived(object sender, DataReceivedEventArgs e) => err += e.Data;
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

        public static (string stdOutput, string errOutput, int exitCode) ExecuteCommand(string command, string arguments, bool debug = false, Action<string>? logger = null)
        {
            var log = logger ?? (s => CALog.LogData(LogID.A, s));

            if (debug)
            {
                log("");
                log(@"\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\");
                log($"Running command: {command} {arguments}");
            }
            string? errorOutput = string.Empty;
            var p = new Process()
            {
                StartInfo = new ProcessStartInfo()
                {
                    FileName = command,
                    Arguments = arguments,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    WorkingDirectory = Environment.CurrentDirectory
                }
            };
            p.ErrorDataReceived += (sender, e) => { errorOutput += e.Data + Environment.NewLine; };
            if (!p.Start())
                throw new InvalidOperationException($"Unable to start process with command: {command} {arguments}");

            // To avoid deadlocks, always read the output stream first and then wait.
            // And, if you read both output and error stream use an asynchronous read operation on at least one of them.
            p.BeginErrorReadLine();
            string standardOutput = p.StandardOutput.ReadToEnd().TrimEnd();

            if (!p.WaitForExit(60000))
                throw new TimeoutException($"Timed out waiting for command to exit: {command} {arguments}");

            if (debug)
            {
                log($"ExitCode: {p.ExitCode}");
                log("-----------");
                if (!string.IsNullOrWhiteSpace(standardOutput))
                    log(standardOutput);
                log("-----------");
                if (!string.IsNullOrWhiteSpace(errorOutput))
                    log(errorOutput);
                log(@"//////////////////////////////");
                log("");
            }

            return (standardOutput, errorOutput, p.ExitCode);
        }
    }
}
