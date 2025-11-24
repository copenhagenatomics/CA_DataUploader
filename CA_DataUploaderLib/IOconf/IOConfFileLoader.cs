#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CA_DataUploaderLib.IOconf
{
    internal static class IOconfFileLoader
    {
        public static bool FileExists(string directory)
        {
            var configPath = Path.Combine(directory, "IO.conf");
            return File.Exists(configPath);
        }

        public static (List<string> raw, List<IOconfRow> original, List<IOconfRow> expanded) Load(IIOconfLoader loader, string directory)
        {
            var configPath = Path.Combine(directory, "IO.conf");
            if (!File.Exists(configPath))
            {
                throw new FileNotFoundException($"Could not find the file {configPath}");
            }

            var list = File.ReadAllLines(configPath).ToList();
            var (original, expanded) = ParseLines(loader, list);
            return (list, original, expanded);
        }

        public static (List<IOconfRow> original, List<IOconfRow> expanded) ParseLines(IIOconfLoader loader, IEnumerable<string> lines)
        {
            var linesList = lines.Select(x => x.Trim()).ToList();
            // remove empty lines and commented out lines
            var lines2 = linesList.Where(x => !x.StartsWith("//") && x.Length > 2).Select((x,i) => (row: x,line: i)).ToList();
            var rows = lines2.Select(x => CreateType(loader, x.row, x.line + 1)).ToList();
            var tags = rows.SelectMany(r => r.Tags.Select(t => (tag: t, row: r))).ToLookup(r => r.tag.name, r=> r.row);
            foreach (var row in rows)
                row.UseTags(tags);
            var expanded = rows.Concat(
                rows.SelectMany(r => r.GetExpandedConfRows().Select(l => CreateType(loader, l, r.LineNumber))))
                .ToList();
            return (rows, expanded);
        }

        /// <summary>
        /// Writes the supplied contents to a file IO.conf on disk.
        /// Renames any existing configuration to IO.conf.backup1 (trailing number increasing).
        /// </summary>
        /// <param name="ioconf"></param>
        public static void WriteToDisk(string ioconf)
        {
            var filename = "IO.conf";
            if (File.Exists(filename))
            {
                var count = 1;
                string newFilename;
                do
                {
                    newFilename = filename + ".backup" + count++;
                }
                while (File.Exists(newFilename));

                File.Copy(filename, newFilename);
            }
            var temp = Path.GetTempFileName();
            File.WriteAllText(temp, ioconf);
            File.Move(temp, filename, true);
        }

        private static IOconfRow CreateType(IIOconfLoader confLoader, string row, int lineNum)
        {
            try
            {
                var separatorIndex = row.IndexOf(';');
                var rowType = separatorIndex > -1 ? row.AsSpan()[..separatorIndex].Trim() : row;
                var loader = confLoader.GetLoader(rowType) ?? ((r, l) => new IOconfRow(r, l, "Unknown", true));
                return loader(row, lineNum);
            }
            catch (Exception ex)
            {
                CALog.LogErrorAndConsoleLn(LogID.A, $"ERROR in line {lineNum} of {Directory.GetCurrentDirectory()}\\IO.conf {Environment.NewLine}{row}{Environment.NewLine}" + ex.ToString());
                throw;
            }
        }
    }
}