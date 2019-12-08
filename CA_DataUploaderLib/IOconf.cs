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

    public class Out230VacInfo
    {
        public string Name;
        public string USBPort;
        public int PortNumber;
        public List<string> row;
        public IOTypes Type;
    }

    public class IOconf
    {
        public List<List<string>> Table { get; private set; }

        public IOconf()
        {
            if (File.Exists("IO.conf"))
            {
                var lines = File.ReadAllLines("IO.conf").ToList();
                lines = lines.Where(x => !x.Trim().StartsWith("//") && x.Trim().Length > 2).Select(x => x.Trim()).ToList();
                Table = lines.Select(x => x.Split(";".ToCharArray()).ToList()).ToList();
                CheckRules(lines);
            }
        }

        private void CheckRules(List<string> lines)
        {
            // all rows in IO.conf file must have at least 3 params. (type;name;other)
            foreach (var row in Table.Where(x => x.Count() < 3))
                CALog.LogErrorAndConsole(LogID.A, $"ERROR: too few parameters in: {lines[Table.IndexOf(row)]}{Environment.NewLine}");

            // no two rows can have the same type,name combination. 
            var groups = Table.GroupBy(x => x[0] + x[1]);
            foreach (var g in groups.Where(x => x.Count() > 1))
                CALog.LogErrorAndConsole(LogID.A, $"ERROR: Name: {g.First()[1]} occure {g.Count()} times in this group: {g.First()[0]}{Environment.NewLine}");

            // all rows of type input/output must have a group which are listed in the map table. 
            var InputOutput = Enum.GetValues(typeof(IOTypes)).Cast<IOTypes>().Skip(3).Select(x => x.ToString()).ToList();
            var MapNames = GetTypes(IOTypes.Map).Select(x => x[2]).ToList();
            foreach (var row in Table.Where(x => InputOutput.Contains(x[0])))
            {
                if(!MapNames.Contains(row[2]))
                    CALog.LogErrorAndConsole(LogID.A, $"ERROR: name of port or box used in row, which was not found in map: {lines[Table.IndexOf(row)]}{Environment.NewLine}");
            }
        }

        public IEnumerable<List<string>> GetTypes(IOTypes type)
        {
            if (Table == null) return new List<List<string>>();

            var list = Table.Where(x => x.First() == type.ToString()).ToList();
            CALog.LogData(LogID.A, $"read {list.Count} {type} rows from IO.config{Environment.NewLine}");
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

        public static CALogLevel GetOutputLevel()
        {
            CALogLevel logLevel;
            var loop = new IOconf().Table.Single(x => x.First() == "LoopName");
            if (Enum.TryParse(loop[2], true, out logLevel))
                return logLevel;

            return CALogLevel.Normal;
        }

        public static IEnumerable<List<string>> GetMap()
        {
            return new IOconf().GetTypes(IOTypes.Map);
        }

        public static IEnumerable<List<string>> GetTypeK()
        {
            return new IOconf().GetTypes(IOTypes.TypeK);
        }

        public static IEnumerable<List<string>> GetOut230Vac()
        {
            return new IOconf().GetTypes(IOTypes.Out230Vac);
        }

        public static Out230VacInfo GetOut230Vac(string name)
        {
            var ioconf = new IOconf();
            var list = ioconf.GetTypes(IOTypes.Out230Vac).Concat(ioconf.GetTypes(IOTypes.Heater)).Concat(ioconf.GetTypes(IOTypes.Valve)).ToList();
            var out230 = list.SingleOrDefault(x => x[1] == name);
            if (out230 == null)
                throw new Exception($"IOConf: unable to find {name}");

            var map = ioconf.GetTypes(IOTypes.Map).Single(x => x[2] == out230[2]);
            var port = RpiVersion.IsWindows() ? map[1] : "/dev/" + map[1];

            return new Out230VacInfo
            {
                Name = name,
                PortNumber = int.Parse(out230[3]),
                USBPort = port,
                row = out230,
                Type = (IOTypes)Enum.Parse(typeof(IOTypes), out230[0])
            };
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

        public static IEnumerable<List<string>> GetMotor()
        {
            return new IOconf().GetTypes(IOTypes.Motor);
        }

        public static IEnumerable<List<string>> GetScale()
        {
            return new IOconf().GetTypes(IOTypes.Scale);
        }

        public static IEnumerable<List<string>> GetValve()
        {
            return new IOconf().GetTypes(IOTypes.Valve);
        }

        public static IEnumerable<List<string>> GetHeater()
        {
            return new IOconf().GetTypes(IOTypes.Heater);
        }
    }
}
