using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace CA_DataUploaderLib
{
    public static class SwitchBoardBase
    {
        private const string _SwitchBoxPattern = "P1=(\\d\\.\\d\\d)A P2=(\\d\\.\\d\\d)A P3=(\\d\\.\\d\\d)A P4=(\\d\\.\\d\\d)A";
        private static Match _LatestRead;
        private static DateTime _lastTimeStamp;

        public static List<double> ReadInputFromSwitchBoxes(MCUBoard box)
        {
            string lines = string.Empty;
            try
            {
                // try to read some text. 
                lines = box.SafeReadExisting();

                // see if it matches the BoxPattern.
                var match = Regex.Match(lines, _SwitchBoxPattern);

                // if match, then store this value for later unsucessful reads. 
                if (match.Success)
                {
                    _LatestRead = match;
                    _lastTimeStamp = DateTime.Now;
                }
                else
                {
                    if (_lastTimeStamp.AddSeconds(2) > DateTime.Now) // if it is less than 2 seconds old, then return last read. 
                        match = _LatestRead;
                }

                double dummy;
                if (match.Success)
                {
                    return match.Groups.Cast<Group>().Skip(1)
                        .Where(x => double.TryParse(x.Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out dummy))
                        .Select(x => double.Parse(x.Value, CultureInfo.InvariantCulture)).ToList();
                }
            }
            catch(Exception ex)
            {
                CALog.LogException(LogID.A, ex);
            }

            CALog.LogData(LogID.B, "ReadInputFromSwitchBoxes: " + lines + Environment.NewLine);
            return new List<double>();  // empty list
        }
    }
}
