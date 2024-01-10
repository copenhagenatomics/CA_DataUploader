using CA_DataUploaderLib;
using CA_DataUploaderLib.Helpers;
using System;
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
            try
            {
                CALog.LogInfoAndConsoleLn(LogID.A, RpiVersion.GetWelcomeMessage($"Upload temperature data to cloud"));
                Console.WriteLine("Initializing...");
                Redundancy.RegisterSystemExtensions();
                using (var serial = new SerialNumberMapper())
                {
                    await serial.DetectDevices();

                    if (args.Length > 0 && args[0] == "-listdevices")
                        return; // SerialNumberMapper already lists devices, no need for further output.

                    // close all ports which are not Hub10
                    serial.McuBoards.OfType<MCUBoard>().Where(x => x.ProductType?.Contains("Temperature") != true && x.ProductType?.Contains("Hub10STM") != true).ToList().ForEach(x => x.SafeClose(System.Threading.CancellationToken.None).Wait());

                    var email = IOconfSetup.UpdateIOconf(serial);

                    using var cmd = new CommandHandler(serial);
                    var cloud = new ServerUploader(cmd);
                    _ = new Redundancy(cmd);
                    _ = new ThermocoupleBox(cmd);
                    var runTask = SingleNodeRunner.Run(cmd, cloud, cmd.StopToken);
                    await Task.Delay(2000);
                    _ = Task.Run(async () => DULutil.OpenUrl(await cloud.GetPlotUrl(cmd.StopToken)));
                    await runTask;
                }
                CALog.LogInfoAndConsoleLn(LogID.A, Environment.NewLine + "Bye..." + Environment.NewLine + "Press any key to exit");
            }
            catch (Exception ex)
            {
                ShowHumanErrorMessages(ex);
            }

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
