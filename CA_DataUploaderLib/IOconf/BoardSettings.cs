using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using CA_DataUploaderLib.Extensions;

namespace CA_DataUploaderLib.IOconf
{
    public class BoardSettings
    {
        public static BoardSettings Default { get; } = new BoardSettings();

        public int DefaultBaudRate { get; set; } = 0;
        public int MillisecondsBetweenReads { get; set; } = 100;
        public int ExpectedHeaderLines { get; set; } = 8;
        public int MaxMillisecondsWithoutNewValues { get; set; } = 2000;
        public int SecondsBetweenReopens { get; set; } = 3;
        public bool StopWhenLosingSensor { get; set; } = true;
        public bool SkipBoardAutoDetection { get; set; } = false;
        public LineParser Parser { get; set; } = LineParser.Default;
        public string ValuesEndOfLineChar { get; set; } = "\n";

        public class LineParser
        {
            public static LineParser Default { get; } = new LineParser();
            private static readonly Regex _startsWithDigitRegex = new Regex(@"^\s*(-|\d+)\s*");

            /// <returns>the list of doubles, or null when the line did not match the expected format</returns>
            public virtual List<double> TryParseAsDoubleList(string line)
            {
                if (!_startsWithDigitRegex.IsMatch(line))
                    return null;

                return line.Split(",".ToCharArray())
                    .Select(x => x.Trim())
                    .Where(x => x.Length > 0)
                    .Select(x => x.ToDouble())
                    .ToList();
            }

            public virtual bool MatchesValuesFormat(string line) => _startsWithDigitRegex.IsMatch(line);

            public virtual bool IsExpectedNonValuesLine(string line) => false;
        }
    }
}