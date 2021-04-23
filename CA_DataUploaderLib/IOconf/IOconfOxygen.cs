using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using CA_DataUploaderLib.Extensions;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfOxygen : IOconfInput
    {
        public IOconfOxygen(string row, int lineNum) : base(
            row, lineNum, "Oxygen", false, true, 
            new BoardSettings() 
            {
                DefaultBaudRate = 9600,
                ExpectedHeaderLines = 1,
                MaxMillisecondsWithoutNewValues = 10000,
                MillisecondsBetweenReads = 1200,
                SecondsBetweenReopens = 10,
                StopWhenLosingSensor = false,
                Parser = LineParser.Default
            })
        {
            format = "Oxygen;Name;BoxName";
        }

        /// <summary>get expanded conf entries that include the oxygen %, oxygen partial pressure and error</summary>
        /// <remarks>the returned entries have port numbers that correspond to the ones returned by the LineParser</remarks>
        public IEnumerable<IOconfOxygen> GetExpandedConf() => new [] {
            new IOconfOxygen($"Oxygen_Oxygen%;{Name};{BoxName}", LineNumber) { PortNumber = 3},
            new IOconfOxygen($"Oxygen_OxygenPartialPressure;{Name};{BoxName}", LineNumber) { PortNumber = 0},
            new IOconfOxygen($"Oxygen_Error;{Name};{BoxName}", LineNumber) { PortNumber = 4}
            };

        public class LineParser : BoardSettings.LineParser
        {
            // "O 0213.1 T +21.0 P 1019 % 020.92 e 0000"
            private const string _luminoxPattern = "O ((?:[0-9]*[.])?[0-9]+) T ([+-]?(?:[0-9]*[.])?[0-9]+) P ((?:[0-9]*[.])?[0-9]+) % ((?:[0-9]*[.])?[0-9]+) e ([0-9]*)";
            private readonly Regex _luminoxRegex = new Regex(_luminoxPattern);
            public new static LineParser Default { get; } = new LineParser();

            // returns partial pressure, temperature, pressure, oxygen %, error code
            public override List<double> TryParseAsDoubleList(string line)
            {
                var match = _luminoxRegex.Match(line);
                if (!match.Success) 
                    return null; 

                var list = match.Groups.Cast<Group>().Skip(1)
                    .Where(x => x.Value.TryToDouble(out _))
                    .Select(x => x.Value.ToDouble()).ToList();
                return new List<double> { list[0], list[1], (list[2] - 1000) / 1000.0, list[3], list[4] };
            }

            public override bool MatchesValuesFormat(string line) => _luminoxRegex.IsMatch(line);
        }
    }
}