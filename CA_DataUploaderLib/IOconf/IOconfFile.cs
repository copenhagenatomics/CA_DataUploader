#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfFile : IIOconf
    {
        private readonly List<IOconfRow> Table = new();
        public List<string> RawLines { get; private set; } = new();

        private static readonly Lazy<IOconfFile> lazy = new(() => new IOconfFile());
        public static IIOconf Instance => lazy.Value;

        public IOconfFile()
        {
            Reload();
        }

        public IOconfFile(List<string> rawLines)
        {
            Table.AddRange(IOconfFileLoader.ParseLines(rawLines));
            RawLines = rawLines;
            Table.ForEach(e => e.ValidateDependencies(this));
            CheckRules();
        }

        public void Reload()
        {
            // the separate IOconfFileLoader can be used by callers to expand the IOconfFile before the IOconfFile initialization rejects the custom entries.
            Table.Clear();
            var (rawLines, entries) = IOconfFileLoader.Load();
            Table.AddRange(entries);
            RawLines = rawLines;
            Table.ForEach(e => e.ValidateDependencies(this));
            CheckRules();
        }

        private void CheckRules()
        {
            // no two rows can have the same type,name combination. 
            var groups = Table.GroupBy(x => x.UniqueKey());
            foreach (var g in groups.Where(x => x.Count() > 1))
                CALog.LogErrorAndConsoleLn(LogID.A, $"ERROR in {Directory.GetCurrentDirectory()}\\IO.conf:{Environment.NewLine} Name: {g.First().ToList()[1]} occurred {g.Count()} times in this group: {g.First().ToList()[0]}");

            // no heater can be in several oven areas
            var heaters = GetOven().Where(x => x.OvenArea > 0).GroupBy(x => x.HeatingElement);
            foreach (var heater in heaters.Where(x => x.Select(y => y.OvenArea).Distinct().Count() > 1))
                CALog.LogErrorAndConsoleLn(LogID.A, $"ERROR in {Directory.GetCurrentDirectory()}\\IO.conf:{Environment.NewLine} Heater: {heater.Key.Name} occurred in several oven areas : {string.Join(", ", heater.Select(y => y.OvenArea).Distinct())}");
        }

        private IOconfLoopName GetLoopConfig() => GetEntries<IOconfLoopName>().SingleOrDefault() ?? IOconfLoopName.Default;

        public ConnectionInfo GetConnectionInfo()
        {
            try
            {
                var loopConfig = GetLoopConfig();
                var account = ((IOconfAccount)Table.Single(x => x.GetType() == typeof(IOconfAccount)));
                return new ConnectionInfo(loopConfig.Name, loopConfig.Server, account.Name, account.Email, account.Password);
            }
            catch (Exception ex)
            {
                throw new Exception($"Did you forgot to include login information in top of {Directory.GetCurrentDirectory()}\\IO.conf ?", ex);
            }
        }

        public string GetLoopName() => GetLoopConfig().Name;
        public string GetLoopServer() => GetLoopConfig().Server;

        public int GetVectorUploadDelay() => GetEntries<IOconfSamplingRates>().SingleOrDefault()?.VectorUploadDelay ?? 1000;
        public int GetMainLoopDelay() => GetEntries<IOconfSamplingRates>().SingleOrDefault()?.MainLoopDelay ?? 200;
        public CALogLevel GetOutputLevel() => GetLoopConfig().LogLevel;
        public IEnumerable<IOconfMap> GetMap() => GetEntries<IOconfMap>();
        public IEnumerable<IOconfGeneric> GetGeneric() => GetEntries<IOconfGeneric>();
        public IEnumerable<IOconfGenericOutput> GetGenericOutputs() => GetEntries<IOconfGenericOutput>();
        public IEnumerable<IOconfTemp> GetTemp() => GetEntries<IOconfTemp>();
        public IOconfRPiTemp GetRPiTemp() => GetEntries<IOconfRPiTemp>().SingleOrDefault() ?? IOconfRPiTemp.Default;
        public IEnumerable<IOconfHeater> GetHeater() => GetEntries<IOconfHeater>();
        public IEnumerable<IOconfOven> GetOven() => GetEntries<IOconfOven>();
        public IEnumerable<IOconfAlert> GetAlerts() => GetEntries<IOconfAlert>();
        public IEnumerable<IOconfMath> GetMath() => GetEntries<IOconfMath>();
        public IEnumerable<IOconfFilter> GetFilters() => GetEntries<IOconfFilter>();
        public IEnumerable<IOconfOutput> GetOutputs() => GetEntries<IOconfOutput>();
        public IEnumerable<IOconfState> GetStates() => GetEntries<IOconfState>();
        public IEnumerable<IOconfInput> GetInputs() => GetEntries<IOconfInput>();
        public IEnumerable<T> GetEntries<T>() => Table.OfType<T>();
        public string GetRawFile() => string.Join(Environment.NewLine, RawLines);
        ///<remarks> filters and math it returns the board state of all their sources.</remarks>
        public IEnumerable<string> GetBoardStateNames(string sensor)
        {
            var sensorsChecked = new HashSet<string>();
            return GetBoardStateNamesForSensors(new[] { sensor }, sensorsChecked);

            IEnumerable<string> GetBoardStateNamesForSensors(IEnumerable<string> sensors, HashSet<string> sensorsChecked)
            {
                var newSensors = sensors.ToHashSet();
                newSensors.ExceptWith(sensorsChecked);
                sensorsChecked.UnionWith(sensors);
                foreach (var input in GetInputs())
                {
                    if (newSensors.Contains(input.Name))
                        yield return input.BoardStateSensorName;
                }

                foreach (var filter in GetFilters())
                {
                    if (!newSensors.Contains(filter.NameInVector)) continue;
                    foreach (var boardState in GetBoardStateNamesForSensors(filter.SourceNames, sensorsChecked))
                        yield return boardState;
                }

                foreach (var math in GetMath())
                {
                    if (!newSensors.Contains(math.Name)) continue;
                    foreach (var boardState in GetBoardStateNamesForSensors(math.SourceNames, sensorsChecked))
                        yield return boardState;
                }
            }
        }
    }
}
