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
        private static Queue<string> _debugQueue = new Queue<string>();
        private static CALogLevel _logLevel = CALogLevel.None;

        public static List<double> ReadInputFromSwitchBoxes(MCUBoard box)
        {
            if (box == null)
                return new List<double>(); // empty

            if (_logLevel == CALogLevel.None)
                _logLevel = IOconfFile.GetOutputLevel();

            string lines = string.Empty;
            try
            {
                // try to read some text. 
                lines = box.SafeReadExisting();
                lock (_debugQueue)
                {
                    _debugQueue.Enqueue(lines);

                    if (DateTime.UtcNow.Subtract(_lastTimeStamp).TotalMilliseconds > 500)
                    {
                        CALog.LogData(LogID.B, $"ReadInputFromSwitchBoxes: '{string.Join("§", _debugQueue)}'{Environment.NewLine}");
                        _debugQueue.Clear();
                    }

                    while (_debugQueue.Count > 10)
                    {
                        _debugQueue.Dequeue();
                    }
                }

                // see if it matches the BoxPattern.
                Match match = Regex.Match(lines, _SwitchBoxPattern);

                lock (_SwitchBoxPattern)
                {
                    // if match, then store this value for later unsucessful reads. 
                    if (match.Success && match.Groups.Count > 4)
                    {
                        _LatestRead = match;
                        _lastTimeStamp = DateTime.UtcNow;
                    }
                    else
                    {
                        if (_LatestRead == null)
                            return new List<double>();

                        if (_lastTimeStamp.AddSeconds(2) > DateTime.UtcNow) // if it is less than 2 seconds old, then return last read. 
                            match = _LatestRead;
                    }

                    if (match.Success && match.Groups.Count > 4)
                    {
                        return match.Groups.Cast<Group>().Skip(1)
                            .Select(x => double.Parse(x.Value, CultureInfo.InvariantCulture)).ToList();
                    }
                }
            }
            catch (Exception ex)
            {
                CALog.LogException(LogID.B, ex);
                // box.SafeClose();  // I don't know if this will solve the problem. 
            }
            
            return new List<double>();  // empty list
        }
    }
}
