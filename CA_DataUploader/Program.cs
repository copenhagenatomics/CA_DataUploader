﻿using CA_DataUploaderLib;
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
                var serial = new SerialNumberMapper(true);
                var dataLoggers = serial.ByFamily("Temperature");
                if (!dataLoggers.Any())
                {
                    CALog.LogInfoAndConsoleLn(LogID.A, "Tempearture sensors not initialized");
                    return;
                }

                CALog.LogInfoAndConsoleLn(LogID.A, RpiVersion.GetWelcomeMessage($"Upload temperature data to cloud{Environment.NewLine}From {dataLoggers.Count()} hubarg16 boards"));

                int filterLen = (args.Count() > 1)?int.Parse(args[1]):10;
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
                            Console.WriteLine($"\r {i}"); // we don't want this in the log file. 
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
