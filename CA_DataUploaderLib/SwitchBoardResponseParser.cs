using System;
using System.Linq;
using System.Text.RegularExpressions;
using CA_DataUploaderLib.Extensions;

namespace CA_DataUploaderLib
{
    public class SwitchBoardResponseParser
    {
        private const string _SwitchBoxPattern = "P1=(-?\\d\\.\\d\\d)A P2=(-?\\d\\.\\d\\d)A P3=(-?\\d\\.\\d\\d)A P4=(-?\\d\\.\\d\\d)A(?: ([01]), ([01]), ([01]), ([01])(?:, (-?\\d+.\\d\\d))?)?";
        private static readonly Regex _switchBoxCurrentsRegex = new Regex(_SwitchBoxPattern);

        public static bool TryParse(string lines, out (double[] currents, bool[] states, double temperature) values)
        {
            var match = _switchBoxCurrentsRegex.Match(lines);
            if (match.Success)
                values = GetValuesFromGroups(match.Groups);
            else
                values = (new double[0], new bool[0], 10000);
                
            return match.Success;
        }

        private static (double[] currents, bool[] states, double temperature) GetValuesFromGroups(GroupCollection groups)
        {
            var valueGroups = groups.Cast<Group>().Skip(1).Where(x => x.Success);
            double[] currents = valueGroups.Take(4).Select(x => x.Value.ToDouble()).ToArray();
            bool[] states = valueGroups.Skip(4).Take(4).Select(x => Convert.ToBoolean(int.Parse(x.Value))).ToArray(); // array is empty if there were no matches for the states groups
            double temperature = valueGroups.Skip(8).FirstOrDefault()?.Value?.ToDouble() ?? 10000; // 10k if there was no match for temperature
            return (currents, states, temperature);
        }
    }
}