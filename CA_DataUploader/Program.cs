using CA_DataUploaderLib;
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

                using (var serial = new SerialNumberMapper())
                {
                    // close all ports which are not Hub10
                    serial.McuBoards.Where(x => !x.productType.Contains("Temperature")).ToList().ForEach(x => x.Close());

                    var email = UpdateIOconf(serial);

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
                                Console.Write($"\r {i++}"); // we don't want this in the log file. 
                            }

                            Thread.Sleep(100);
                            if (i == 20) OpenUrl("https://www.copenhagenatomics.com/plots/?" + email);
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

        private static string UpdateIOconf(SerialNumberMapper serial)
        {
            if (!File.Exists("IO.conf"))
                throw new Exception($"Could not find the file {Directory.GetCurrentDirectory()}\\IO.conf");

            var lines = File.ReadAllLines("IO.conf").ToList();
            if (lines.Any(x => x.StartsWith("LoopName")) && lines.Any(x => x.StartsWith("Account")) && lines.Any(x => x.StartsWith("Map")) && lines.Count(x => x.StartsWith("TypeK")) > 1)
            {
                Console.WriteLine("Do you want to skip IO.conf setup? (yes, no)");
                var answer = Console.ReadLine();
                if (answer.ToLower().StartsWith("y"))
                {
                    if (lines.Any(x => x.StartsWith("Account")))
                    {
                        var user = lines.First(x => x.StartsWith("Account")).Split(";".ToCharArray(), StringSplitOptions.RemoveEmptyEntries).ToList();
                        return user[2].Trim(); // return the email address. 
                    }

                    return "";
                }
            }
            else
            {
                // comment out all the existing Map lines
                foreach (var mapLine in lines.Where(x => x.StartsWith("Map")).ToList())
                {
                    int j = lines.IndexOf(mapLine);
                    lines[j] = "// " + lines[j];
                }
            }

            // add new Map lines
            int i = 1;
            foreach (var mcu in serial.McuBoards.Where(x => !x.serialNumber.StartsWith("unknown")))
            {
                lines.Insert(4, $"Map;{mcu.serialNumber};ThermalBox{i++}");
            }

            if (lines.Any(x => x.StartsWith("LoopName")))
                lines.Remove(lines.First(x => x.StartsWith("LoopName")));

            Console.WriteLine("Please enter a name for the webchart ");
            Console.WriteLine("It must be a new name you have not used before: ");
            var plotname = Console.ReadLine();

            lines.Insert(0, $"LoopName;{plotname};Normal;https://www.theng.dk");

            if (lines.Any(x => x.StartsWith("Account")))
                lines.Remove(lines.First(x => x.StartsWith("Account")));

            Console.WriteLine("Please enter your full name: ");
            var fullname = Console.ReadLine();
            string email;
            do
            {
                Console.WriteLine("Please enter your email address: ");
                email = Console.ReadLine();
            } 
            while (!IsValidEmail(email));

            Console.WriteLine("Please enter a password for the webchart: ");
            var pwd = Console.ReadLine();

            lines.Insert(1, $"Account; {fullname}; {email}; {pwd}");

            File.Delete("IO.conf");
            File.WriteAllLines("IO.conf", lines);
            ReloadIOconf(serial);

            if (serial.McuBoards.Count > 1)
                Console.WriteLine("You need to manually edit the IO.conf file and add more 'TypeK' lines..");

            return email;
        }

        private static bool IsValidEmail(string email)
        {
            try
            {
                MailAddress m = new MailAddress(email);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        private static void ReloadIOconf(SerialNumberMapper serial)
        {
            IOconfFile.Reload();
            foreach (var board in serial.McuBoards)
            {
                foreach (var ioconfMap in IOconfFile.GetMap())
                {
                    if (ioconfMap.SetMCUboard(board))
                        board.BoxName = ioconfMap.BoxName;
                }
            }
        }

        private static VectorDescription GetVectorDescription(ThermocoupleBox usb)
        {
            var list = usb.GetVectorDescriptionItems();
            CALog.LogInfoAndConsoleLn(LogID.A, $"{list.Count.ToString().PadLeft(2)} datapoints from {usb.Title}");
            return new VectorDescription(list, RpiVersion.GetHardware(), RpiVersion.GetSoftware());
        }

        private static void OpenUrl(string url)
        {
            try
            {
                Process.Start(url);
            }
            catch
            {
                // hack because of this: https://github.com/dotnet/corefx/issues/10361
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    url = url.Replace("&", "^&");
                    Process.Start(new ProcessStartInfo("cmd", $"/c start {url}") { CreateNoWindow = true });
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    Process.Start("xdg-open", url);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    Process.Start("open", url);
                }
                else
                {
                    throw;
                }
            }
        }
    }
}
