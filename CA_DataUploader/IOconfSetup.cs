using CA_DataUploaderLib;
using CA_DataUploaderLib.Helpers;
using CA_DataUploaderLib.IOconf;
using System;
using System.IO;
using System.Linq;
using System.Net.Mail;

namespace CA_DataUploader
{
    public class IOconfSetup
    {
        public static string UpdateIOconf(IIOconf ioconf, SerialNumberMapper serial)
        {
            var mcuList = serial.McuBoards.Where(x => x.SerialNumber?.StartsWith("unknown") != true);
            if (!mcuList.Any())
                throw new Exception($"Could not find any devices connected to USB.");

            if (!File.Exists("IO.conf"))
                throw new Exception($"Could not find the file {Directory.GetCurrentDirectory()}\\IO.conf");

            var lines = File.ReadAllLines("IO.conf").ToList();
            if (lines.Any(x => x.StartsWith("LoopName")) && lines.Any(x => x.StartsWith("Account")) && lines.Any(x => x.StartsWith("Map")) && lines.Count(x => x.StartsWith("TypeK")) > 1)
            {
                Console.WriteLine("Do you want to skip IO.conf setup? (yes, no)");
                var answer = Console.ReadLine() ?? throw new NotSupportedException("failed to answer from console input");
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

            // comment out all the existing Map lines
            foreach (var mapLine in lines.Where(x => x.StartsWith("Map")).ToList())
            {
                int j = lines.IndexOf(mapLine);
                lines[j] = "// " + lines[j];
            }

            // add new Map lines
            int i = 1;
            foreach (var mcu in mcuList)
            {
                lines.Insert(4, $"Map;{mcu.SerialNumber};ThermalBox{i++}");
            }

            if (lines.Any(x => x.StartsWith("LoopName")))
                lines.Remove(lines.First(x => x.StartsWith("LoopName")));

            Console.Write("Please enter a name for the webchart (It must be a new name you have not used before): ");
            var plotname = Console.ReadLine() ?? throw new NotSupportedException("failed to answer from console input");
            if (plotname.Length > 50)
                plotname = plotname[..50];

            lines.Insert(0, $"LoopName;{plotname};Normal;https://www.theng.dk");

            if (lines.Any(x => x.StartsWith("Account")))
                lines.Remove(lines.First(x => x.StartsWith("Account")));

            Console.Write("Please enter your full name: ");
            var fullname = Console.ReadLine() ?? throw new NotSupportedException("failed to answer from console input");
            if (fullname.Length > 100)
                fullname = fullname[..100];

            string email;
            Console.Write("Please enter your email address: ");
            do
            {
                email = Console.ReadLine() ?? throw new NotSupportedException("failed to answer from console input");
            }
            while (!IsValidEmail(email));

            string pwd;
            Console.Write("Please enter a password for the webchart: ");
            do
            {
                pwd = Console.ReadLine() ?? throw new NotSupportedException("failed to answer from console input");
            }
            while (!IsValidPassword(pwd));

            lines.Insert(1, $"Account; {fullname}; {email}; {pwd}");

            File.Delete("IO.conf");
            File.WriteAllLines("IO.conf", lines);
            ReloadIOconf(ioconf, serial);

            if (mcuList.Count() > 1)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("You need to manually edit the IO.conf file and add more 'TypeK' lines..");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                DULutil.OpenUrl("https://github.com/copenhagenatomics/CA_DataUploader/wiki/IO.conf-documentation");
            }

            return email;
        }

        private static bool IsValidEmail(string email)
        {
            try
            {
                if (email.Length > 50)
                {
                    Console.WriteLine("email is too long, please try again: ");
                    return false;
                }
                MailAddress m = new(email);
                return true;
            }
            catch (FormatException)
            {
                Console.WriteLine("invalid email address, please try again: ");
                return false;
            }
        }

        private static bool IsValidPassword(string pwd)
        {
            if (pwd.Length < 6)
            {
                Console.WriteLine("password must be at least 6 characters long, please try again: ");
                return false;
            }

            return true;
        }

        private static void ReloadIOconf(IIOconf ioconf, SerialNumberMapper serial)
        {
            ioconf.Reload();
            foreach (var board in serial.McuBoards)
            foreach (var ioconfMap in ioconf.GetMap())
                board.TrySetMap(ioconfMap);
        }
    }
}
