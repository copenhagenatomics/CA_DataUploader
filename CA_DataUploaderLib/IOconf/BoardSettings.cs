#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Text.RegularExpressions;
using CA_DataUploaderLib.Extensions;

namespace CA_DataUploaderLib.IOconf
{
    public partial class BoardSettings
    {
        public static BoardSettings Default { get; } = new BoardSettings();

        public int DefaultBaudRate { get; set; } = 0;
        public int MillisecondsBetweenReads { get; set; } = 100;
        public int MaxMillisecondsWithoutNewValues { get; set; } = 2000;
        public int SecondsBetweenReopens { get; set; } = 3;
        public bool SkipBoardAutoDetection { get; set; } = false;
        public LineParser Parser { get; set; } = LineParser.Default;
        public char ValuesEndOfLineChar { get; set; } = '\n';
        public string? Calibration { get; set; }
        public bool SkipCalibrationWhenHeaderIsMissing { get; set; } = false;

        ///<remarks>index is 0 based</remarks>
        public void SetCalibrationAtIndex(string defaultCalibration, char value, int index)
        {
            var currCalibration = Calibration ?? defaultCalibration;
            Calibration = string.Create(currCalibration.Length, currCalibration, (chars, currCalibration) => 
            {
                for (int i = 0; i < chars.Length; i++)
                    chars[i] = (i == index) ? value : currCalibration[i];
            });
        }

        public partial class LineParser
        {
            public static LineParser Default { get; } = new LineParser();
            protected static readonly Regex _hasCommaSeparatedNumbers = HasCommaSeparatedNumbersRegex();

            /// <returns>the list of doubles, or null when the line did not match the expected format</returns>
            public virtual (List<double>?, uint) TryParseAsDoubleList(string line)
            {
                var status = 0U;
                var lineMatch = _hasCommaSeparatedNumbers.Match(line);
                if (!lineMatch.Success)
                    return (null, status);

                if (lineMatch.Groups["Status"].Success)
                    uint.TryParse(lineMatch.Groups["Status"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out status);

                return (lineMatch.Groups["Values"].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.ToDouble())
                    .ToList(), status);
            }

            public virtual bool MatchesValuesFormat(string line) => _hasCommaSeparatedNumbers.IsMatch(line);

            public virtual bool IsExpectedNonValuesLine(string line) => false;

            [GeneratedRegex(@"^\s*(?<Values>[+-]?(?:[0-9]*[.])?[0-9]+\s*(?:,\s*[+-]?(?:[0-9]*[.])?[0-9]+\s*)*)(?:,\s*0x(?<Status>[0-9a-fA-F]+))?,?\s*$")]
            private static partial Regex HasCommaSeparatedNumbersRegex();
        }
    }
}