using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CA_DataUploaderLib.IOconf
{
    public static class IOconfFile
    {
        private static readonly List<IOconfRow> Table = new List<IOconfRow>();
        public static List<string> RawLines { get; private set; }

        static IOconfFile()
        {
            if (!Table.Any())
            {
                Reload();
            }
        }

        public static void Reload()
        { 
            // the separate IOconfFileLoader can be used by callers to expand the IOconfFile before the IOconfFile initialization / static ctor rejects the custom entries.
            Table.Clear();
            var (rawLines, entries) = IOconfFileLoader.Load();
            Table.AddRange(entries);
            RawLines = rawLines;
            CheckRules();
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

        private static IOconfLoopName GetLoopConfig() => GetEntries<IOconfLoopName>().SingleOrDefault() ?? IOconfLoopName.Default;

        public static ConnectionInfo GetConnectionInfo()
        {
            try
            {
                var loopConfig = GetLoopConfig();
                var account = ((IOconfAccount)Table.Single(x => x.GetType() == typeof(IOconfAccount)));
                return new ConnectionInfo
                {
                    LoopName = loopConfig.Name,
                    Server = loopConfig.Server,
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

        public static string GetLoopName() => GetLoopConfig().Name;
        public static int GetVectorUploadDelay() => GetEntries<IOconfSamplingRates>().SingleOrDefault()?.VectorUploadDelay ?? 1000;
        public static int GetMainLoopDelay() => GetEntries<IOconfSamplingRates>().SingleOrDefault()?.MainLoopDelay ?? 200;
        public static CALogLevel GetOutputLevel() => GetLoopConfig().LogLevel;
        public static IEnumerable<IOconfMap> GetMap() => GetEntries<IOconfMap>();
        public static IEnumerable<IOconfGeneric> GetGeneric()  => GetEntries<IOconfGeneric>();
        public static IEnumerable<IOconfTypeK> GetTypeK() => GetEntries<IOconfTypeK>();
        public static IOconfRPiTemp GetRPiTemp() => GetEntries<IOconfRPiTemp>().SingleOrDefault() ?? IOconfRPiTemp.Default;
        public static IEnumerable<IOconfInput> GetGeiger()=> GetEntries<IOconfGeiger>();
        public static IEnumerable<IOconfInput> GetAirFlow()=> GetEntries<IOconfAirFlow>();
        public static IEnumerable<IOconfHeater> GetHeater() => GetEntries<IOconfHeater>();
        public static IEnumerable<IOconfLight> GetLight() => GetEntries<IOconfLight>();
        public static IEnumerable<IOconfOven> GetOven() => GetEntries<IOconfOven>();
        public static IEnumerable<IOconfAlert> GetAlerts()=> GetEntries<IOconfAlert>();
        public static IEnumerable<IOconfMath> GetMath() => GetEntries<IOconfMath>();
        public static IEnumerable<IOconfFilter> GetFilters() => GetEntries<IOconfFilter>();
        public static IEnumerable<IOconfOutput> GetOutputs() => GetEntries<IOconfOutput>();
        public static IEnumerable<IOconfState> GetStates() => GetEntries<IOconfState>();
        public static IEnumerable<IOconfInput> GetInputs() => GetEntries<IOconfInput>();
        public static IEnumerable<T> GetEntries<T>() => Table.OfType<T>();
        public static string GetRawFile() => string.Join(Environment.NewLine, RawLines);
    }
}
