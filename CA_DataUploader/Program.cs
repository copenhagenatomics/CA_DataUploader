using CA_DataUploaderLib;
using System;
using System.Collections.Generic;
using System.Linq;
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

                var serial = new SerialNumberMapper(true);
                var dataLoggers = serial.ByProductType("Temperature");
                if (!dataLoggers.Any())
                {
                    CALog.LogInfoAndConsoleLn(LogID.A, "Tempearture sensors not initialized");
                    return;
                }

                // close all ports which are not temperature sensors. 
                serial.McuBoards.ToList().ForEach(x => { if (x.productType.StartsWith("Switch") || x.productType.StartsWith("Relay")) x.Close(); });

                int filterLen = (args.Length > 0)?int.Parse(args[0]):10;
                using (var usb = new CAThermalBox(dataLoggers, filterLen))
                using(var cloud = new ServerUploader(usb.GetVectorDescription()))
                {
                    CALog.LogInfoAndConsoleLn(LogID.A, "Now connected to server");

                    int i = 0;
                    while (!UserPressedEscape())
                    {
                        var allSensors = usb.GetAllValidTemperatures().OrderBy(x => x.ID).ToList();
                        if (allSensors.Any())
                        {
                            cloud.SendVector(allSensors.Select(x => x.Temperature).ToList(), AverageSensorTimestamp(allSensors));
                            Console.Write($"\r {i}"); // we don't want this in the log file. 
                            i += 1;
                        }

                        Thread.Sleep(100);
                        if (i==20) CALog.LogInfoAndConsoleLn(LogID.A, cloud.PrintMyPlots());
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



        private static bool UserPressedEscape()
        {
            if (!Console.KeyAvailable)
                return false;

            return Console.ReadKey(true).Key == ConsoleKey.Escape;
        }

        private static DateTime AverageSensorTimestamp(IEnumerable<TermoSensor> allTermoSensors)
        {
            return new DateTime((long)allTermoSensors.Average(x => (double)x.TimeStamp.Ticks));
        }
    }
}
