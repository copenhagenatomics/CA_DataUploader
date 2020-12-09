using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CA_DataUploaderLib.IOconf
{
    public static class IOconfFile
    {
        private static readonly List<IOconfRow> Table = new List<IOconfRow>();
        public static string RawFile { get; private set; }

        static IOconfFile()
        {
            if (!Table.Any())
            {
                Reload();
            }
        }

        public static void Reload()
        {
            Table.Clear();
            if (!File.Exists("IO.conf"))
                throw new Exception($"Could not find the file {Directory.GetCurrentDirectory()}\\IO.conf");

            var lines = File.ReadAllLines("IO.conf").ToList();
            RawFile = string.Join(Environment.NewLine, lines);
            // remove empty lines and commented out lines
            var lines2 = lines.Where(x => !x.Trim().StartsWith("//") && x.Trim().Length > 2).Select(x => x.Trim()).ToList();
            lines2.ForEach(x => Table.Add(CreateType(x, lines.IndexOf(x))));
            CheckRules();
        }

        private static IOconfRow CreateType(string row, int lineNum)
        {
            try
            {
                if (row.StartsWith("LoopName")) return new IOconfLoopName(row, lineNum);
                if (row.StartsWith("Account")) return new IOconfAccount(row, lineNum);
                if (row.StartsWith("Map"))    return new IOconfMap(row, lineNum);
                if (row.StartsWith("Math")) return new IOconfMath(row, lineNum);
                if (row.StartsWith("Alert")) return new IOconfAlert(row, lineNum);
                if (row.StartsWith("TypeK"))  return new IOconfTypeK(row, lineNum);
                if (row.StartsWith("SaltLeakage")) return new IOconfSaltLeakage(row, lineNum);
                if (row.StartsWith("AirFlow")) return new IOconfAirFlow(row, lineNum);
                if (row.StartsWith("Heater")) return new IOconfHeater(row, lineNum);
                if (row.StartsWith("Light")) return new IOconfLight(row, lineNum);
                if (row.StartsWith("Motor")) return new IOconfMotor(row, lineNum);
                if (row.StartsWith("Oven")) return new IOconfOven(row, lineNum);
                if (row.StartsWith("Pressure")) return new IOConfPressure(row, lineNum);
                if (row.StartsWith("Geiger")) return new IOConfGeiger(row, lineNum);
                if (row.StartsWith("Scale")) return new IOconfScale(row, lineNum);
                if (row.StartsWith("Tank")) return new IOconfTank(row, lineNum);
                if (row.StartsWith("Valve")) return new IOconfValve(row, lineNum);
                if (row.StartsWith("Filter")) return new IOconfFilter(row, lineNum);
                if (row.StartsWith("RPiTemp")) return new IOconfRPiTemp(row, lineNum);
                if (row.StartsWith("VacuumPump")) return new IOconfVacuumPump(row, lineNum);
                if (row.StartsWith("Oxygen")) return new IOconfOxygen(row, lineNum);

                return new IOconfRow(row, lineNum, "Unknown");
            }
            catch (Exception ex)
            {
                CALog.LogErrorAndConsoleLn(LogID.A, $"ERROR in line {lineNum} of {Directory.GetCurrentDirectory()}\\IO.conf {Environment.NewLine}{row}{Environment.NewLine}" + ex.ToString());
                throw;
            }
        }

        private static void CheckRules()
        {
            // no two rows can have the same type,name combination. 
            var groups = Table.GroupBy(x => x.UniqueKey());
            foreach (var g in groups.Where(x => x.Count() > 1))
                CALog.LogErrorAndConsoleLn(LogID.A, $"ERROR in {Directory.GetCurrentDirectory()}\\IO.conf:{Environment.NewLine} Name: {g.First().ToList()[1]} occure {g.Count()} times in this group: {g.First().ToList()[0]}{Environment.NewLine}");

            // no heater can be in several oven areas
            var heaters = GetOven().Where(x => x.OvenArea > 0).GroupBy(x => x.HeatingElement);
            foreach(var heater in heaters.Where(x => x.Select(y => y.OvenArea).Distinct().Count() > 1))
                CALog.LogErrorAndConsoleLn(LogID.A, $"ERROR in {Directory.GetCurrentDirectory()}\\IO.conf:{Environment.NewLine} Heater: {heater.Key.Name} occure in several oven areas : {string.Join(", ", heater.Select(y => y.OvenArea).Distinct())}");
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
                throw new Exception($"Did you forgot to include login information in top of {Directory.GetCurrentDirectory()}\\IO.conf ?", ex);
            }
        }

        public static CALogLevel GetOutputLevel()
        {
            if (!Table.Any()) return CALogLevel.None;
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

        public static IEnumerable<IOconfSaltLeakage> GetSaltLeakage()
        {
            return Table.Where(x => x.GetType() == typeof(IOconfSaltLeakage)).Cast<IOconfSaltLeakage>();
        }

        public static IEnumerable<IOconfInput> GetTypeKAndLeakageAndRPiTemp()
        {
            return Table.Where(x => x.GetType() == typeof(IOconfTypeK) || x.GetType() == typeof(IOconfSaltLeakage) || x.GetType() == typeof(IOconfRPiTemp)).Cast<IOconfInput>();
        }

        public static IEnumerable<IOconfOut230Vac> GetOut230Vac()
        {
            return Table.Where(x => x.GetType() == typeof(IOconfOut230Vac)).Cast<IOconfOut230Vac>();
        }

        public static IEnumerable<IOconfInput> GetPressure()
        {
            return Table.Where(x => x.GetType() == typeof(IOConfPressure)).Cast<IOconfInput>();
        }

        public static IEnumerable<IOconfInput> GetGeiger()
        {
            return Table.Where(x => x.GetType() == typeof(IOConfGeiger)).Cast<IOconfInput>();
        }

        public static IEnumerable<IOconfInput> GetAirFlow()
        {
            return Table.Where(x => x.GetType() == typeof(IOconfAirFlow)).Cast<IOconfInput>();
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

        public static IEnumerable<IOconfOxygen> GetOxygen()
        {
            return Table.Where(x => x.GetType() == typeof(IOconfOxygen)).Cast<IOconfOxygen>();
        }

        public static IEnumerable<IOconfOven> GetOven()
        {
            return Table.Where(x => x.GetType() == typeof(IOconfOven)).Cast<IOconfOven>();
        }

        public static IEnumerable<IOconfAlert> GetAlerts()
        {
            return Table.Where(x => x.GetType() == typeof(IOconfAlert)).Cast<IOconfAlert>();
        }

        public static IEnumerable<IOconfMath> GetMath()
        {
            return Table.Where(x => x.GetType() == typeof(IOconfMath)).Cast<IOconfMath>();
        }

        public static IEnumerable<IOconfFilter> GetFilters()
        {
            return Table.Where(x => x.GetType() == typeof(IOconfFilter)).Cast<IOconfFilter>();  
        }

        public static IEnumerable<IOconfInput> GetInputs() => Table.OfType<IOconfInput>();
    }
}
