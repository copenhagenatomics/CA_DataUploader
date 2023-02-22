#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CA_DataUploaderLib.IOconf
{
    public static class IOconfFileLoader
    {
        private readonly static List<(string rowType, Func<string, int, IOconfRow> loader)> Loaders = new()
        {
            ("LoopName", (r, l) => new IOconfLoopName(r, l)),
            ("Account", (r, l) => new IOconfAccount(r, l)),
            ("SampleRates", (r, l) => new IOconfSamplingRates(r, l)),
            ("Map", (r, l) => new IOconfMap(r, l)),
            ("Math", (r, l) => new IOconfMath(r, l)),
            ("Alert", (r, l) => new IOconfAlert(r, l)),
            (IOconfTemp.TypeKName, IOconfTemp.NewTypeK),
            (IOconfTemp.TypeJName, IOconfTemp.NewTypeJ),
            ("Heater", (r, l) => new IOconfHeater(r, l)),
            ("Oven", (r, l) => new IOconfOven(r, l)),
            ("Filter", (r, l) => new IOconfFilter(r, l)),
            ("RPiTemp", (r, l) => new IOconfRPiTemp(r, l)),
            ("GenericSensor", (r, l) => new IOconfGeneric(r, l)),
            ("SwitchboardSensor", (r, l) => new IOconfSwitchboardSensor(r, l)),
            ("Node", (r, l) => new IOconfNode(r, l)),
            ("Code", (r, l) => new IOconfCode(r, l)),
        };

        public static (List<string>, IEnumerable<IOconfRow>) Load()
        {
            if (!File.Exists("IO.conf"))
            {
                throw new Exception($"Could not find the file {Directory.GetCurrentDirectory()}\\IO.conf");
            }

            var list = File.ReadAllLines("IO.conf").ToList();
            return (list, ParseLines(list));
        }

        public static IEnumerable<IOconfRow> ParseLines(IEnumerable<string> lines)
        {
            var linesList = lines.ToList();
            // remove empty lines and commented out lines
            var lines2 = linesList.Where(x => !x.Trim().StartsWith("//") && x.Trim().Length > 2).Select(x => x.Trim()).ToList();
            return lines2.Select(x => CreateType(x, linesList.IndexOf(x)));
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
                var loader = GetLoader(rowType) ?? ((r, l) => new IOconfRow(r, l, "Unknown", true));
                return loader(row, lineNum);
            }
            catch (Exception ex)
            {
                CALog.LogErrorAndConsoleLn(LogID.A, $"ERROR in line {lineNum} of {Directory.GetCurrentDirectory()}\\IO.conf {Environment.NewLine}{row}{Environment.NewLine}" + ex.ToString());
                throw;
            }
        }

        private static Func<string, int, IOconfRow>? GetLoader(ReadOnlySpan<char> rowType)
        {
            foreach (var loader in Loaders)
                if (rowType.Equals(loader.rowType, StringComparison.InvariantCultureIgnoreCase))
                    return loader.loader;

            return null;
        }
    }
}