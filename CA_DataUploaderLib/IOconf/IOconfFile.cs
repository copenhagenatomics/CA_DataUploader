#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfFile : IIOconf
    {
        private List<IOconfRow> Table = [];
        private List<IOconfRow> OriginalRows = [];
        private Dictionary<string, string> CodeRepoURLs;
        public List<string> RawLines { get; private set; } = [];

        private static readonly Lazy<IOconfFile> lazy = new(() => new IOconfFile());
        public static IIOconf Instance => lazy.Value;
        public static IIOconfLoader DefaultLoader { get; } = new IOconfLoader();
        public static bool FileExists() => IOconfFileLoader.FileExists();

        public IOconfFile(string? directory = null)
        {
            Reload(directory);
        }

        public IOconfFile(List<string> rawLines, bool performCheck = true)
            : this(DefaultLoader, rawLines, performCheck) { }
        public IOconfFile(IIOconfLoader loader, List<string> rawLines, bool performCheck = true)
        {
            (rawLines, CodeRepoURLs) = IOconfCodeRepo.ExtractAndHideURLs(rawLines, IOconfCodeRepo.ReadURLsFromFile());
            (OriginalRows, Table) = IOconfFileLoader.ParseLines(loader, rawLines);
            RawLines = rawLines;
            EnsureRPiTempEntry();
            if (performCheck)
                CheckConfig();
        }

        [MemberNotNull(nameof(CodeRepoURLs))]
        public void Reload(string? directory = null)
        {
            CodeRepoURLs = IOconfCodeRepo.ReadURLsFromFile(directory);
            // the separate IOconfFileLoader can be used by callers to expand the IOconfFile before the IOconfFile initialization rejects the custom entries.
            (RawLines, OriginalRows, Table) = IOconfFileLoader.Load(DefaultLoader, directory);
            EnsureRPiTempEntry();
            CheckConfig();
        }

        /// <summary>
        /// Writes the configuration to a file IO.conf on disk.
        /// Renames any existing configuration to IO.conf.backup1 (trailing number increasing).
        /// Also writes the code repository URLs to a file on disk.
        /// </summary>
        /// <param name="ioconf"></param>
        public void WriteToDisk()
        {
            IOconfFileLoader.WriteToDisk(GetRawFile());
            IOconfCodeRepo.WriteURLsToFile(CodeRepoURLs);
        }

        public void CheckConfig()
        {
            CheckUniquenessRule();
            // Work-around for dealing with Redundant-lines after everything else
            Table.Where(e => e is not Redundancy.IOconfRedundant).ToList().ForEach(e => e.ValidateDependencies(this));
            Table.Where(e => e is Redundancy.IOconfRedundant).ToList().ForEach(e => e.ValidateDependencies(this));
            CheckMapLineUniquenessRules();
            CheckOvenHeaterRelationshipRule();
        }

        private void EnsureRPiTempEntry()
        {//this is mostly here to ease rename support for hosts that support renaming the rows in the configuration.
            if (OriginalRows.OfType<IOconfRPiTemp>().Any())
                return;
            OriginalRows.Add(IOconfRPiTemp.Default);
            Table.Add(IOconfRPiTemp.Default);
            RawLines.Add(IOconfRPiTemp.Default.Row);
        }

        private void CheckUniquenessRule()
        {
            // no two rows can have the same type,name combination. 
            var groups = Table.GroupBy(x => x.UniqueKey());
            var errorMessage = string.Empty;
            foreach (var g in groups.Where(x => x.Count() > 1))
                errorMessage += (!string.IsNullOrEmpty(errorMessage) ? Environment.NewLine : "") + $"Duplicate configuration key {g.Key} detected. Lines involved:{Environment.NewLine}{string.Join(Environment.NewLine, g.Select(r => r.Row).ToList())}";
            if (!string.IsNullOrEmpty(errorMessage))
                throw new FormatException(errorMessage);
        }

        private void CheckMapLineUniquenessRules()
        {
            var errorMessage = string.Empty;
            // No two map lines can have the same serial number
            foreach (var mapLine in GetMap().GroupBy(m => m.SerialNumber).Where(x => x.Key is not null && x.Count() > 1))
                errorMessage += (!string.IsNullOrEmpty(errorMessage) ? Environment.NewLine : "") + $"Two Map-lines cannot use the same serial number. Lines involved:{Environment.NewLine}{string.Join(Environment.NewLine, mapLine.Select(y => y.Row))}";
            // No two map lines on the same node can have the same port
            foreach (var mapLine in GetMap().GroupBy(m => new { m.DistributedNode, m.USBPort }).Where(x => x.Key.USBPort is not null && x.Count() > 1))
            errorMessage += (!string.IsNullOrEmpty(errorMessage) ? Environment.NewLine : "") + $"Two Map-lines for the same node cannot use the same port. Lines involved:{Environment.NewLine}{string.Join(Environment.NewLine, mapLine.Select(y => y.Row))}";
            if (!string.IsNullOrEmpty(errorMessage))
                throw new FormatException(errorMessage);
        }

        private void CheckOvenHeaterRelationshipRule()
        {
            // no heater can be in several oven areas
            var heaters = GetOven().Where(x => x.OvenArea > 0).GroupBy(x => x.HeatingElement);
            var errorMessage = string.Empty;
            foreach (var heater in heaters.Where(x => x.Count() > 1))
                errorMessage += (!string.IsNullOrEmpty(errorMessage) ? Environment.NewLine : "") + $"Duplicate Oven lines detected for the same heater. Lines involved:{Environment.NewLine}{string.Join(Environment.NewLine, heater.Select(y => y.Row))}";
            if (!string.IsNullOrEmpty(errorMessage))
                throw new FormatException(errorMessage);
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
                throw new FormatException($"Did you forget to include login information in top of {Directory.GetCurrentDirectory()}\\IO.conf ?", ex);
            }
        }

        public string GetLoopName() => GetLoopConfig().Name;
        public string GetLoopServer() => GetLoopConfig().Server;

        public int GetVectorUploadDelay() => GetEntries<IOconfSamplingRates>().SingleOrDefault()?.VectorUploadDelay ?? 1000;
        public int GetMainLoopDelay() => GetEntries<IOconfSamplingRates>().SingleOrDefault()?.MainLoopDelay ?? 100;
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
        public IEnumerable<T> GetEntriesWithoutExpansion<T>() => OriginalRows.OfType<T>();
        public string GetRawFile() => string.Join(Environment.NewLine, RawLines);

        ///<remarks> 
        ///Filter and math returns the board state of all their sources.
        ///Only to be called after ValidateDependencies has been called on all rows.
        ///</remarks>
        public IEnumerable<string> GetBoardStateNames(string sensor)
        {
            var sensorsChecked = new HashSet<string>();
            return GetBoardStateNamesForSensors([sensor], sensorsChecked);

            IEnumerable<string> GetBoardStateNamesForSensors(IEnumerable<string> sensors, HashSet<string> sensorsChecked)
            {
                var newSensors = sensors.ToHashSet();
                newSensors.ExceptWith(sensorsChecked);
                sensorsChecked.UnionWith(sensors);
                foreach (var input in GetEntries<IOconfRow>().Where(e => e is IIOconfRowWithBoardState))
                {
                    if (newSensors.Intersect(input.GetExpandedSensorNames()).Any())
                        yield return ((IIOconfRowWithBoardState)input).BoardStateName;
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

        public Dictionary<string, string> GetCodeRepoURLs() => CodeRepoURLs;
        public void ValidateCodeRepoURLs()
        {
            foreach (var codeRepo in GetEntries<IOconfCodeRepo>())
                codeRepo.LoadURL(this);
        }
    }
}
