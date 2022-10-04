using CA_DataUploaderLib;
using CA_DataUploaderLib.Helpers;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace CA_DataUploader
{
    class Program
    {
        static void Main(string[] args)
        {
            MainAsync(args).GetAwaiter().GetResult();
        }

        static async Task MainAsync(string[] args)
        {
            Queue<string> receivedCommandsInThisCycleQueue = new();
            List<string> receivedCommandsInThisCycle = new();

            try
            {
                CALog.LogInfoAndConsoleLn(LogID.A, RpiVersion.GetWelcomeMessage($"Upload temperature data to cloud"));
                Console.WriteLine("Initializing...");
                using (var serial = await SerialNumberMapper.DetectDevices())
                {
                    if (args.Length > 0 && args[0] == "-listdevices")
                        return; // SerialNumberMapper already lists devices, no need for further output.

                    // close all ports which are not Hub10
                    serial.McuBoards.Where(x => !x.productType.Contains("Temperature") &&!x.productType.Contains("Hub10STM")).ToList().ForEach(x => x.SafeClose(System.Threading.CancellationToken.None).Wait());

                    var email = IOconfSetup.UpdateIOconf(serial);

                    using var cmd = new CommandHandler(serial);
                    _ = new ThermocoupleBox(cmd);
                    using var cloud = new ServerUploader(cmd.GetFullSystemVectorDescription(), cmd);
                    cmd.EventFired += cloud.SendEvent;
                    cmd.EventFired += AddToReceivedCommandsQueue;
                    CALog.LogInfoAndConsoleLn(LogID.A, "Now connected to server...");
                    cmd.Execute("help");
                    _ = Task.Run(() => cmd.RunSubsystems());

                    int i = 0;
                    var uploadThrottle = new TimeThrottle(100);
                    DataVector dataVector = null;
                    while (cmd.IsRunning)
                    {
                        cmd.GetFullSystemVectorValues(ref dataVector, GetReceivedCommandsInThisCycle());
                        cloud.SendVector(dataVector);
                        Console.Write($"\r data points uploaded: {i++}"); // we don't want this in the log file. 
                        uploadThrottle.Wait();
                        if (i == 20) DULutil.OpenUrl(cloud.GetPlotUrl());
                    }
                }
                CALog.LogInfoAndConsoleLn(LogID.A, Environment.NewLine + "Bye..." + Environment.NewLine + "Press any key to exit");
            }
            catch (Exception ex)
            {
                ShowHumanErrorMessages(ex);
            }

            Console.ReadKey();

            void AddToReceivedCommandsQueue(object source, EventFiredArgs args)
            {
                lock (receivedCommandsInThisCycleQueue)
                {
                    if (args.EventType == (byte)EventType.Command)
                        return;

                    receivedCommandsInThisCycleQueue.Enqueue(args.Data);
                }
            }

            List<string> GetReceivedCommandsInThisCycle()
            {
                lock (receivedCommandsInThisCycleQueue)
                {
                    receivedCommandsInThisCycle.Clear();
                    while (receivedCommandsInThisCycleQueue.TryDequeue(out var command))
                        receivedCommandsInThisCycle.Add(command);

                    return receivedCommandsInThisCycle;
                }
            }
        }

        private static void ShowHumanErrorMessages(Exception ex)
        {
            if (ex.Message.StartsWith("account already exist"))
            {
                CALog.LogErrorAndConsoleLn(LogID.A, "Your password was wrong. Please exit and try again..");
                CALog.LogInfoAndConsoleLn(LogID.A, Environment.NewLine + "Press any key to exit");
            }
            else if (ex.Message.StartsWith("Could not find any devices connected to USB"))
            {
                CALog.LogErrorAndConsoleLn(LogID.A, ex.Message + " Please check USB connections and try again..");
                CALog.LogInfoAndConsoleLn(LogID.A, Environment.NewLine + "Press any key to exit");
            }
            else if (ex.InnerException != null && ex.InnerException.Message.Contains("LoopName already used before:"))
            {
                CALog.LogErrorAndConsoleLn(LogID.A, "Please change your Webchart name and try again..");
                CALog.LogInfoAndConsoleLn(LogID.A, Environment.NewLine + "Press any key to exit");
            }
            else
            {
                CALog.LogException(LogID.A, ex);
            }
        }
    }
}
