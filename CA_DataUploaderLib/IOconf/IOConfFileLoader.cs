#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CA_DataUploaderLib.IOconf
{
    public static class IOconfFileLoader
    {
        public static IIOconfLoader Loader { get; } = new IOconfLoader();

        public static bool FileExists()
        {
            return File.Exists("IO.conf");
        }

        public static (List<string>, IEnumerable<IOconfRow>) Load()
        {
            if (!FileExists())
            {
                throw new Exception($"Could not find the file {Directory.GetCurrentDirectory()}\\IO.conf");
            }

            var list = File.ReadAllLines("IO.conf").ToList();
            return (list, ParseLines(Loader, list));
        }

        public static IEnumerable<IOconfRow> ParseLines(IIOconfLoader loader, IEnumerable<string> lines)
        {
            IOconfNode.ResetIndex();
            IOconfCode.ResetIndex();

            var linesList = lines.Select(x => x.Trim()).ToList();
            // remove empty lines and commented out lines
            var lines2 = linesList.Where(x => !x.StartsWith("//") && x.Length > 2).Select((x,i) => (row: x,line: i)).ToList();
            var rows = lines2.Select(x => CreateType(loader, x.row, x.line + 1)).ToList();
            var tags = rows.SelectMany(r => r.Tags.Select(t => (tag: t, rowname: r.Name))).ToLookup(r => r.tag, r=> r.rowname);
            foreach (var row in rows)
                row.UseTags(tags);
            return rows.Concat(rows.SelectMany(r => r.GetExpandedConfRows().Select(l => CreateType(loader, l, r.LineNumber))));
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

                File.Move(filename, newFilename);
            }
            File.WriteAllText(filename, ioconf);
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