using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CA_DataUploaderLib.IOconf
{
    public static class IOconfFileLoader
    {
        private readonly static List<(string rowType, Func<string, int, IOconfRow> loader)> Loaders = new List<(string rowType, Func<string, int, IOconfRow> ctor)>
        {
            ("LoopName", (r, l) => new IOconfLoopName(r, l)),
            ("Account", (r, l) => new IOconfAccount(r, l)),
            ("SampleRates", (r, l) => new IOconfSamplingRates(r, l)),
            ("Map", (r, l) => new IOconfMap(r, l)),
            ("Math", (r, l) => new IOconfMath(r, l)),
            ("Alert", (r, l) => new IOconfAlert(r, l)),
            ("TypeK", (r, l) => new IOconfTypeK(r, l)),
            ("SaltLeakage", (r, l) => new IOconfSaltLeakage(r, l)),
            ("AirFlow", (r, l) => new IOconfAirFlow(r, l)),
            ("Heater", (r, l) => new IOconfHeater(r, l)),
            ("Light", (r, l) => new IOconfLight(r, l)),
            ("Oven", (r, l) => new IOconfOven(r, l)),
            ("Geiger", (r, l) => new IOconfGeiger(r, l)),
            ("Filter", (r, l) => new IOconfFilter(r, l)),
            ("RPiTemp", (r, l) => new IOconfRPiTemp(r, l)),
            ("VacuumPump", (r, l) => new IOconfVacuumPump(r, l)),
            ("GenericSensor", (r, l) => new IOconfGeneric(r, l)),
            ("SwitchboardSensor", (r, l) => new IOconfSwitchboardSensor(r, l)),
            ("Node", (r, l) => new IOconfNode(r, l)),
        };

        public static (string, IEnumerable<IOconfRow>) Load(List<IOconfRow> target)
        {
            if (!File.Exists("IO.conf"))
                throw new Exception($"Could not find the file {Directory.GetCurrentDirectory()}\\IO.conf");

            return ParseLines(File.ReadAllLines("IO.conf"));
        }

        public static (string, IEnumerable<IOconfRow>) ParseLines(IEnumerable<string> lines)
        {
            var linesList = lines.ToList();
            var rawFile = string.Join(Environment.NewLine, linesList);
            // remove empty lines and commented out lines
            var lines2 = linesList.Where(x => !x.Trim().StartsWith("//") && x.Trim().Length > 2).Select(x => x.Trim()).ToList();
            return (rawFile, lines2.Select(x => CreateType(x, linesList.IndexOf(x))));
        }

        public static void AddLoader(string rowType, Func<string, int, IOconfRow> loader)
        {
            if (GetLoader(rowType) != null)
                throw new ArgumentException($"the specified loader rowType is already in use: {rowType}", nameof(rowType));

            Loaders.Add((rowType, loader));
        }

        private static IOconfRow CreateType(string row, int lineNum)
        {
            try
            {
                var separatorIndex = row.IndexOf(';');
                var rowType = separatorIndex > -1 ? row.AsSpan()[..separatorIndex].Trim() : row;
                var loader = GetLoader(rowType) ?? ((r, l) => new IOconfRow(r, l, "Unknown"));
                return loader(row, lineNum);
            }
            catch (Exception ex)
            {
                CALog.LogErrorAndConsoleLn(LogID.A, $"ERROR in line {lineNum} of {Directory.GetCurrentDirectory()}\\IO.conf {Environment.NewLine}{row}{Environment.NewLine}" + ex.ToString());
                throw;
            }
        }

        private static Func<string, int, IOconfRow> GetLoader(ReadOnlySpan<char> rowType)
        {
            foreach (var loader in Loaders)
                if (rowType.Equals(loader.rowType, StringComparison.InvariantCultureIgnoreCase))
                    return loader.loader;

            return null;
        }
    }
}