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

                    using (var cmd = new CommandHandler())
                    using (var usb = new ThermocoupleBox(cmd, new TimeSpan(0, 0, 1)))
                    using (var cloud = new ServerUploader(GetVectorDescription(usb), cmd))
                    {
                        CALog.LogInfoAndConsoleLn(LogID.A, "Now connected to server...");

                        int i = 0;
                        while (cmd.IsRunning)
                        {
                            var allSensors = usb.GetAllDatapoints().ToList();
                            if (allSensors.Any())
                            {
                                var list = allSensors.Select(x => x.Value).ToList();
                                list.AddRange(usb.GetFrequencyAndFilterCount());
                                cloud.SendVector(list, allSensors.First().TimeStamp);
                                Console.Write($"\r data points uploaded: {i++}"); // we don't want this in the log file. 
                            }

                            Thread.Sleep(100);
                            if (i == 20) DULutil.OpenUrl("https://www.copenhagenatomics.com/plots/?" + email);
                        }
                    }
                }
                CALog.LogInfoAndConsoleLn(LogID.A, Environment.NewLine + "Bye..." + Environment.NewLine + "Press any key to exit");
            }
            catch (Exception ex)
            {
                CALog.LogException(LogID.A, ex);
            }

            Console.ReadKey();
        }

        private static VectorDescription GetVectorDescription(ThermocoupleBox usb)
        {
            var list = usb.GetVectorDescriptionItems();
            CALog.LogInfoAndConsoleLn(LogID.A, $"{list.Count.ToString().PadLeft(2)} datapoints from {usb.Title}");
            return new VectorDescription(list, RpiVersion.GetHardware(), RpiVersion.GetSoftware());
        }
    }
}
