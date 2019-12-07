using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CA_DataUploaderLib
{
    public static class SwitchBoardBase
    {
        private const string _SwitchBoxPattern = "P1=(\\d\\.\\d\\d)A P2=(\\d\\.\\d\\d)A P3=(\\d\\.\\d\\d)A P4=(\\d\\.\\d\\d)A";

        public static List<double> ReadInputFromSwitchBoxes(MCUBoard box)
        {
            try
            {
                var lines = box.ReadExisting();
                var match = Regex.Match(lines, _SwitchBoxPattern);
                double dummy;
                if (match.Success)
                    return match.Groups.Cast<Group>().Skip(1)
                        .Where(x => double.TryParse(x.Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out dummy))
                        .Select(x => double.Parse(x.Value, CultureInfo.InvariantCulture)).ToList();
            }
            catch { }

            return new List<double>();  // empty list
        }
    }
}
