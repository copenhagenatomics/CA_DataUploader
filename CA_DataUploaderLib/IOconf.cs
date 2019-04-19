using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CA_DataUploaderLib
{
    public class ConnectionInfo
    {
        public string LoopName;
        public string Server;
        public string Fullname;
        public string email;
        public string password;
    }

    public class IOconf
    {
        public List<List<string>> Table { get; private set; }

        public IOconf()
        {
            var lines = File.ReadAllLines("IO.conf").ToList();
            lines = lines.Where(x => !x.Trim().StartsWith("//") && x.Trim().Length > 2).Select(x => x.Trim()).ToList();
            Table = lines.Select(x => x.Split(",".ToCharArray()).ToList()).ToList();

            foreach (var row in Table.Where(x => x.Count() < 3))
                Console.WriteLine($"ERROR: too few parameters in: {lines[Table.IndexOf(row)]}");

            var groups = Table.GroupBy(x => x[0] + x[1]);
            foreach (var g in groups.Where(x => x.Count() > 1))
                Console.WriteLine($"WARNING: Name: {g.First()[1]} occure {g.Count()} times in this group: {g.First()[0]}");
        }

        public IEnumerable<List<string>> GetTypes(IOTypes type)
        {
            var list = Table.Where(x => x.First() == type.ToString()).ToList();
            Console.WriteLine($"read {list.Count} {type} rows from IO.config");
            return list;
        }

        public static string GetLoopName()
        {
            return new IOconf().Table.Single(x => x.First() == "LoopName")[1];
        }

        public static ConnectionInfo GetConnectionInfo()
        {
            try
            {
                var table = new IOconf().Table;
                return new ConnectionInfo
                {
                    LoopName = table.Single(x => x.First() == "LoopName")[1].Trim(),
                    Server = table.Single(x => x.First() == "LoopName")[3].Trim(),
                    Fullname = table.Single(x => x.First() == "Account")[1].Trim(),
                    email = table.Single(x => x.First() == "Account")[2].Trim(),
                    password = table.Single(x => x.First() == "Account")[3].Trim(),
                };
            }
            catch (Exception ex)
            {
                throw new Exception("Did you forgot to include login information in top of IO.conf ?", ex);
            }
        }

        public static LogLevel GetOutputLevel()
        {
            LogLevel logLevel;
            var loop = new IOconf().Table.Single(x => x.First() == "LoopName");
            if (Enum.TryParse(loop[2], true, out logLevel))
                return logLevel;

            return LogLevel.Normal;
        }

        public static IEnumerable<List<string>> GetInTypeK()
        {
            return new IOconf().GetTypes(IOTypes.InTypeK);
        }

        public static IEnumerable<List<string>> GetOut230Vac()
        {
            return new IOconf().GetTypes(IOTypes.Out230Vac);
        }

        public static IEnumerable<List<string>> GetPressure()
        {
            return new IOconf().GetTypes(IOTypes.Pressure);
        }

        public static IEnumerable<List<string>> GetAirFlow()
        {
            return new IOconf().GetTypes(IOTypes.AirFlow);
        }

        public static IEnumerable<List<string>> GetLiquidFlow()
        {
            return new IOconf().GetTypes(IOTypes.LiquidFlow);
        }

        public static IEnumerable<List<string>> GetMotorSpeed()
        {
            return new IOconf().GetTypes(IOTypes.MotorSpeed);
        }
    }
}
