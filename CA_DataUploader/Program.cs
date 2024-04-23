using CA_DataUploaderLib;
using CA_DataUploaderLib.Helpers;
using CA_DataUploaderLib.IOconf;
using System;
using System.Data;
using System.Linq;
using System.Threading;
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
            var autoStopped = false;
            try
            {
                var loglevel = IOconfFileLoader.FileExists() ? IOconfFile.Instance.GetOutputLevel() : IOconfLoopName.Default.LogLevel;
                CALog.LogInfoAndConsoleLn(LogID.A, RpiVersion.GetWelcomeMessage($"Upload temperature data to cloud", loglevel));
                Console.WriteLine("Initializing...");
                Redundancy.RegisterSystemExtensions(IOconfFileLoader.Loader);
                var serial = new SerialNumberMapper(IOconfFileLoader.FileExists() ? IOconfFile.Instance : null);
                await serial.DetectDevices();

                if (args.Length > 0 && args[0] == "-listdevices")
                    return; // SerialNumberMapper already lists devices, no need for further output.

                // close all ports which are not Hub10
                serial.McuBoards.OfType<MCUBoard>().Where(x => x.ProductType?.Contains("Temperature") != true && x.ProductType?.Contains("Hub10STM") != true).ToList().ForEach(x => x.SafeClose(System.Threading.CancellationToken.None).Wait());

                IOconfSetup.UpdateIOconf(serial);

                var ioconf = IOconfFile.Instance;
                using var cmd = new CommandHandler(ioconf, serial);
                var cloud = new ServerUploader(ioconf, cmd);
                _ = new Redundancy(ioconf, cmd);
                _ = new ThermocoupleBox(ioconf, cmd);
                _ = cmd.GetFullSystemVectorDescription(); //this ensures the vector description is initialized before running the subsystems.
                var runTask = SingleNodeRunner.Run(ioconf, cmd, cloud, cmd.StopToken);
                var plotIdTask = cloud.GetPlotId(cmd.StopToken);
                await Task.WhenAny(cmd.RunningTask, plotIdTask); //wait for a connection to be established
                if (!plotIdTask.IsCompletedSuccessfully)
                {
                  cmd.Execute("escape"); //explicitely stop on connection failures (needed so all the subsystems explicitely stop so awaiting them below is safe)
                  autoStopped = true;
                }
              
                await runTask;  
            }
            catch (Exception ex)
            {
                ShowHumanErrorMessages(ex);
            }

            CALog.LogInfoAndConsoleLn(LogID.A, Environment.NewLine + "Bye..." + Environment.NewLine + "Press any key to exit");
            if (!autoStopped) //when auto stopping the CommandHandler is still waiting for a last key press.
                Console.ReadKey();
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
