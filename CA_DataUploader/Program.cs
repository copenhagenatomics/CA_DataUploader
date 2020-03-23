using CA_DataUploaderLib;
using CA_DataUploaderLib.IOconf;
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
                var ioconf = new IOconfFile();

                using (var serial = new SerialNumberMapper(true))
                {
                    serial.PortsChanged += Serial_PortsChanged;

                    // close all relay board serial ports connections. 
                    IOconfFile.GetOut230Vac().ToList().ForEach(x => x.Map.Board.SafeClose());

                    using (var cmd = new CommandHandler())
                    using (var usb = new ThermocoupleBox(cmd, new TimeSpan(0, 0, 1)))
                    using (var cloud = new ServerUploader(GetVectorDescription(usb), cmd))
                    {
                        CALog.LogInfoAndConsoleLn(LogID.A, "Now connected to server");

                        int i = 0;
                        while (cmd.IsRunning)
                        {
                            var allSensors = usb.GetAllValidDatapoints().ToList();
                            if (allSensors.Any())
                            {
                                cloud.SendVector(allSensors.Select(x => x.Value).ToList(), AverageSensorTimestamp(allSensors));
                                Console.Write($"\r {i}"); // we don't want this in the log file. 
                                i += 1;
                            }

                            Thread.Sleep(100);
                            if (i == 20) CALog.LogInfoAndConsoleLn(LogID.A, cloud.PrintMyPlots());
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

        private static VectorDescription GetVectorDescription(BaseSensorBox usb)
        {
            var list = usb.GetVectorDescriptionItems();
            CALog.LogInfoAndConsoleLn(LogID.A, $"{list.Count.ToString().PadLeft(2)} datapoints from {usb.Title}");
            return new VectorDescription(list, RpiVersion.GetHardware(), RpiVersion.GetSoftware());
        }

        private static void Serial_PortsChanged(object sender, PortsChangedArgs e)
        {
            foreach (var p in e.MCUBoards)
                CALog.LogInfoAndConsoleLn(LogID.A, $"Serial port {(e.EventType == EventType.Insertion?"inserted":"removed")}: {p.ToStringSimple(" ")}");
        }

        private static DateTime AverageSensorTimestamp(IEnumerable<SensorSample> allTermoSensors)
        {
            return new DateTime((long)allTermoSensors.Average(x => (double)x.TimeStamp.Ticks));
        }
    }
}
