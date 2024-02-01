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

            IOconfNode.ResetNodeIndexCount();
            var list = File.ReadAllLines("IO.conf").ToList();
            return (list, ParseLines(list));
        }

        public static IEnumerable<IOconfRow> ParseLines(IEnumerable<string> lines)
        {
            var linesList = lines.Select(x => x.Trim()).ToList();
            // remove empty lines and commented out lines
            var lines2 = linesList.Where(x => !x.StartsWith("//") && x.Length > 2).ToList();
            return lines2.Select(x => CreateType(x, linesList.IndexOf(x)));
        }

        private static IOconfRow CreateType(string row, int lineNum)
        {
            try
            {
                var separatorIndex = row.IndexOf(';');
                var rowType = separatorIndex > -1 ? row.AsSpan()[..separatorIndex].Trim() : row;
                var loader = Loader.GetLoader(rowType) ?? ((r, l) => new IOconfRow(r, l, "Unknown", true));
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