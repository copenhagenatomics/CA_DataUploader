using CA_DataUploaderLib;
using CA_DataUploaderLib.Helpers;
using CA_DataUploaderLib.IOconf;
using System;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Mail;
using System.Runtime.InteropServices;
using System.Threading;

namespace CA_DataUploader
{
    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                CALog.LogInfoAndConsoleLn(LogID.A, RpiVersion.GetWelcomeMessage($"Upload temperature data to cloud"));
                Console.WriteLine("Initializing...");
                using (var serial = new SerialNumberMapper())
                {
                    // close all ports which are not Hub10
                    serial.McuBoards.Where(x => !x.productType.Contains("Temperature")).ToList().ForEach(x => x.Close());

                    var email = IOconfSetup.UpdateIOconf(serial);

                    using (var cmd = new CommandHandler(serial))
                    using (var usb = new ThermocoupleBox(cmd))
                    using (var filterUtil = new FilterUtil(GetVectorDescription(usb)))
                    using (var cloud = new ServerUploader(filterUtil.ExpandedVectorDescription, cmd))
                    {
                        CALog.LogInfoAndConsoleLn(LogID.A, "Now connected to server...");

                        int i = 0;
                        while (cmd.IsRunning)
                        {
                            var allSensors = usb.GetValues().ToList();
                            if (allSensors.Any())
                            {
                                var list = filterUtil.FilterAndMath(allSensors);
                                cloud.SendVector(list, allSensors.First().TimeStamp);
                                Console.Write($"\r data points uploaded: {i++}"); // we don't want this in the log file. 
                            }

                            Thread.Sleep(100);
                            if (i == 20) DULutil.OpenUrl(cloud.GetPlotUrl());
                        }
                    }
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

        private static VectorDescription GetVectorDescription(ThermocoupleBox usb)
        {
            var list = usb.GetVectorDescriptionItems();
            CALog.LogInfoAndConsoleLn(LogID.A, $"{list.Count.ToString().PadLeft(2)} datapoints from {usb.Title}");
            return new VectorDescription(list, RpiVersion.GetHardware(), RpiVersion.GetSoftware());
        }
    }
}
