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
            string com = "/dev/ttyUSB0";
            com = "COM3";

            try
            {
                if(args.Any())
                    com = args[0];

                Console.WriteLine(RpiVersion.GetWelcomeMessage("Upload temperature data to cloud"));

                int filterLen = (args.Count() > 1)?int.Parse(args[1]):10;
                using (var usb = new CAThermalBox(com, 8, filterLen))
                {
                    var cloud = new ServerUploader("http://www.theng.dk", usb.GetVectorDescription());
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

                        Thread.Sleep(1000);
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
