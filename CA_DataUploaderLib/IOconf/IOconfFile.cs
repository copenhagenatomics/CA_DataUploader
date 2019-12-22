using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfFile
    {
        protected static List<IOconfRow> Table = new List<IOconfRow>();

        public IOconfFile()
        {
            if (File.Exists("IO.conf") && !Table.Any())
            {
                var lines = File.ReadAllLines("IO.conf").ToList();
                // remove empty lines and commented out lines
                lines = lines.Where(x => !x.Trim().StartsWith("//") && x.Trim().Length > 2).Select(x => x.Trim()).ToList();
                lines.ForEach(x => Table.Add(CreateType(x)));
                CheckRules(lines);
            }
        }

        private IOconfRow CreateType(string row)
        {
            if (row.StartsWith("LoopName")) return new IOconfLoopName(row);
            if (row.StartsWith("Account")) return new IOconfAccount(row);
            if (row.StartsWith("Map"))    return new IOconfMap(row);
            if (row.StartsWith("TypeK"))  return new IOconfTypeK(row, GetMap());
            if (row.StartsWith("AirFlow")) return new IOconfAirFlow(row, GetMap());
            if (row.StartsWith("Heater")) return new IOconfHeater(row, GetMap());
            if (row.StartsWith("Light")) return new IOconfLight(row, GetMap());
            if (row.StartsWith("LiquidFlow")) return new IOconfLiquidFlow(row, GetMap());
            if (row.StartsWith("Motor")) return new IOconfMotor(row, GetMap());
            if (row.StartsWith("Oven")) return new IOconfOven(row, GetMap());
            if (row.StartsWith("Pressure")) return new IOConfPressure(row, GetMap());
            if (row.StartsWith("Scale")) return new IOconfScale(row, GetMap());
            if (row.StartsWith("Tank")) return new IOconfTank(row, GetMap());
            if (row.StartsWith("Valve")) return new IOconfValve(row, GetMap());
            if (row.StartsWith("VacuumPump")) return new IOconfVacuumPump(row, GetMap());

            return new IOconfRow(row, "Unknown");
        }

        private void CheckRules(List<string> lines)
        {
            // no two rows can have the same type,name combination. 
            var groups = Table.GroupBy(x => x.UniqueKey());
            foreach (var g in groups.Where(x => x.Count() > 1))
                CALog.LogErrorAndConsole(LogID.A, $"ERROR: Name: {g.First().ToList()[1]} occure {g.Count()} times in this group: {g.First().ToList()[0]}{Environment.NewLine}");
        }

        public static string GetLoopName()
        {
            return ((IOconfLoopName)Table.Single(x => x.GetType() == typeof(IOconfLoopName))).Name;
        }

        public static ConnectionInfo GetConnectionInfo()
        {
            try
            {
                var loopName = ((IOconfLoopName)Table.Single(x => x.GetType() == typeof(IOconfLoopName)));
                var account = ((IOconfAccount)Table.Single(x => x.GetType() == typeof(IOconfAccount)));
                return new ConnectionInfo
                {
                    LoopName = loopName.Name,
                    Server = loopName.Server,
                    Fullname = account.Name,
                    email = account.Email,
                    password = account.Password,
                };
            }
            catch (Exception ex)
            {
                throw new Exception("Did you forgot to include login information in top of IO.conf ?", ex);
            }
        }

        public static CALogLevel GetOutputLevel()
        {
            return ((IOconfLoopName)Table.Single(x => x.GetType() == typeof(IOconfLoopName))).LogLevel;
        }

        public static IEnumerable<IOconfMap> GetMap()
        {
            return Table.Where(x => x.GetType() == typeof(IOconfMap)).Cast<IOconfMap>();
        }

        public static IEnumerable<IOconfTypeK> GetTypeK()
        {
            return Table.Where(x => x.GetType() == typeof(IOconfTypeK)).Cast<IOconfTypeK>();
        }

        public static IEnumerable<IOconfOut230Vac> GetOut230Vac()
        {
            return Table.Where(x => x.GetType() == typeof(IOconfOut230Vac)).Cast<IOconfOut230Vac>();
        }

        public static IEnumerable<IOConfPressure> GetPressure()
        {
            return Table.Where(x => x.GetType() == typeof(IOConfPressure)).Cast<IOConfPressure>();
        }

        public static IEnumerable<IOconfAirFlow> GetAirFlow()
        {
            return Table.Where(x => x.GetType() == typeof(IOconfAirFlow)).Cast<IOconfAirFlow>();
        }

        public static IEnumerable<IOconfLiquidFlow> GetLiquidFlow()
        {
            return Table.Where(x => x.GetType() == typeof(IOconfLiquidFlow)).Cast<IOconfLiquidFlow>();
        }

        public static IEnumerable<IOconfMotor> GetMotor()
        {
            return Table.Where(x => x.GetType() == typeof(IOconfMotor)).Cast<IOconfMotor>();
        }

        public static IEnumerable<IOconfScale> GetScale()
        {
            return Table.Where(x => x.GetType() == typeof(IOconfScale)).Cast<IOconfScale>();
        }

        public static IEnumerable<IOconfValve> GetValve()
        {
            return Table.Where(x => x.GetType() == typeof(IOconfValve)).Cast<IOconfValve>();
        }

        public static IEnumerable<IOconfHeater> GetHeater()
        {
            return Table.Where(x => x.GetType() == typeof(IOconfHeater)).Cast<IOconfHeater>();
        }

        public static IEnumerable<IOconfLight> GetLight()
        {
            return Table.Where(x => x.GetType() == typeof(IOconfLight)).Cast<IOconfLight>();
        }
    }
}
