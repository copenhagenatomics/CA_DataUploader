using System.Collections.Generic;
using CA_DataUploaderLib.Extensions;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfScale : IOconfInput
    { 
        public IOconfScale(string row, int lineNum) : base(row, lineNum, "Scale", false, true, 
            new BoardSettings()
            {
                DefaultBaudRate = 9600,
                ExpectedHeaderLines = 1, 
                Parser = LineParser.Default
            }) 
        { 
        }

        private class LineParser : BoardSettings.LineParser
        {
            public new static LineParser Default { get; } = new LineParser();
            
            public override List<double> TryParseAsDoubleList(string line)
            { // expected line format: "+0000.00 kg"
                line = line.TrimEnd('k', 'g');
                return base.TryParseAsDoubleList(line);
            }

            public override bool MatchesValuesFormat(string line) => line.TrimEnd('k', 'g').TryToDouble(out _);
        }
    }
}
