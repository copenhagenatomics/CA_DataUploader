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
                var serial = new SerialNumberMapper(true);
                var dataLoggers = serial.ByFamily("Temperature");
                if (!dataLoggers.Any())
                {
                    Console.WriteLine("Tempearture sensors not initialized");
                    return;
                }

                Console.WriteLine(RpiVersion.GetWelcomeMessage($"Upload temperature data to cloud{Environment.NewLine}From {dataLoggers.Count()} hubarg16 boards"));

                int filterLen = (args.Count() > 1)?int.Parse(args[1]):10;
                using (var usb = new CAThermalBox(dataLoggers, filterLen))
                {
                    var cloud = new ServerUploader(usb.GetVectorDescription());
                    Console.WriteLine("Now connected to server");

                    int i = 0;
                    while (!UserPressedEscape())
                    {
                        var allSensors = usb.GetAllValidTemperatures().OrderBy(x => x.ID).ToList();
                        if (allSensors.Any())
                        {
                            cloud.SendVector(allSensors.Select(x => x.Temperature).ToList(), AverageSensorTimestamp(allSensors));
                            Console.Write($"\r {i++}");
                        }

                        Thread.Sleep(100);
                    }
                }

                Console.WriteLine(Environment.NewLine + "Bye...");
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                Console.WriteLine();
                Console.WriteLine(ex.StackTrace.ToString());
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
