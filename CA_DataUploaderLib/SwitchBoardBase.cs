using CA_DataUploaderLib.IOconf;
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
        private static Match _LatestRead = null;
        private static DateTime _lastTimeStamp = DateTime.MinValue;

        public static List<double> ReadInputFromSwitchBoxes(MCUBoard box)
        {
            string lines = string.Empty;
            try
            {
                // try to read some text. 
                lines = box.SafeReadExisting();

                if (IOconfFile.GetOutputLevel() == CALogLevel.Debug)
                {
                    CALog.LogData(LogID.B, $"{lines}{Environment.NewLine}");
                }

                // see if it matches the BoxPattern.
                Match match = Regex.Match(lines, _SwitchBoxPattern);

                lock (_SwitchBoxPattern)
                {
                    // if match, then store this value for later unsucessful reads. 
                    if (match.Success && match.Groups.Count > 4)
                    {
                        _LatestRead = match;
                        _lastTimeStamp = DateTime.Now;
                    }
                    else
                    {
                        if (_LatestRead == null)
                            return new List<double>();

                        if (_lastTimeStamp.AddSeconds(2) > DateTime.Now) // if it is less than 2 seconds old, then return last read. 
                            match = _LatestRead;
                    }
                }

                if (match.Success && match.Groups.Count > 4)
                {
                    return match.Groups.Cast<Group>().Skip(1)
                        .Select(x => double.Parse(x.Value, CultureInfo.InvariantCulture)).ToList();
                }
            }
            catch(Exception ex)
            {
                CALog.LogException(LogID.B, ex);
                // box.SafeClose();  // I don't know if this will solve the problem. 
            }

            CALog.LogData(LogID.B, $"ReadInputFromSwitchBoxes: '{lines}'{Environment.NewLine}");
            return new List<double>();  // empty list
        }
    }
}
