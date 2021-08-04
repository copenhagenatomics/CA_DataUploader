using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CA_DataUploaderLib.IOconf
{
    public static class IOconfFileLoader
    {
        private readonly static List<(string prefix, Func<string, int, IOconfRow> loader)> Loaders = new List<(string prefix, Func<string, int, IOconfRow> ctor)>
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
            ("Motor", (r, l) => new IOconfMotor(r, l)),
            ("Oven", (r, l) => new IOconfOven(r, l)),
            ("Pressure", (r, l) => new IOconfPressure(r, l)),
            ("Geiger", (r, l) => new IOconfGeiger(r, l)),
            ("Scale", (r, l) => new IOconfScale(r, l)),
            ("Filter", (r, l) => new IOconfFilter(r, l)),
            ("RPiTemp", (r, l) => new IOconfRPiTemp(r, l)),
            ("VacuumPump", (r, l) => new IOconfVacuumPump(r, l)),
            ("Oxygen", (r, l) => new IOconfOxygen(r, l)),
            ("GenericSensor", (r, l) => new IOconfGeneric(r, l))
        };

        public static (string, IEnumerable<IOconfRow>) Load(List<IOconfRow> target)
        {
            if (!File.Exists("IO.conf"))
                throw new Exception($"Could not find the file {Directory.GetCurrentDirectory()}\\IO.conf");

            var lines = File.ReadAllLines("IO.conf").ToList();
            var rawFile = string.Join(Environment.NewLine, lines);
            // remove empty lines and commented out lines
            var lines2 = lines.Where(x => !x.Trim().StartsWith("//") && x.Trim().Length > 2).Select(x => x.Trim()).ToList();
            return (rawFile, lines2.Select(x => CreateType(x, lines.IndexOf(x))));
        }

        public static void AddLoader(string prefix, Func<string, int, IOconfRow> loader)
        {
            if (GetLoader(prefix) != null)
                throw new ArgumentException($"the specified loader prefix is already in use: {prefix}", nameof(prefix));

            Loaders.Add((prefix, loader));
        }

        private static IOconfRow CreateType(string row, int lineNum)
        {
            try
            {
                var loader = GetLoader(row) ?? ((r, l) => new IOconfRow(r, l, "Unknown"));
                return loader(row, lineNum);
            }
            catch (Exception ex)
            {
                CALog.LogErrorAndConsoleLn(LogID.A, $"ERROR in line {lineNum} of {Directory.GetCurrentDirectory()}\\IO.conf {Environment.NewLine}{row}{Environment.NewLine}" + ex.ToString());
                throw;
            }
        }

        private static Func<string, int, IOconfRow> GetLoader(string row)
        {
            foreach (var loader in Loaders)
                if (row.StartsWith(loader.prefix))
                    return loader.loader;

            return null;
        }
    }
}