using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace CA_DataUploaderLib
{
    public static class SwitchBoardBase
    {
        private static readonly ConcurrentDictionary<MCUBoard, SwitchBoardValues> Values = new ConcurrentDictionary<MCUBoard, SwitchBoardValues>();
        public static List<double> ReadInputFromSwitchBoxes(MCUBoard box)
        {
            if (box == null)
                return new List<double>();

            var values = Values.GetOrAdd(box, b => new SwitchBoardValues(b));
            lock (values)
                return values.ReadInputFromSwitchBoxes();
        }

        private class SwitchBoardValues
        {
            private const string _SwitchBoxPattern = "P1=(\\d\\.\\d\\d)A P2=(\\d\\.\\d\\d)A P3=(\\d\\.\\d\\d)A P4=(\\d\\.\\d\\d)A";
            private static readonly Regex _switchBoxCurrentsRegex = new Regex(_SwitchBoxPattern);
            private readonly MCUBoard box;
            private Match _lastValidRead = null;
            private DateTime _lastValidReadTime = DateTime.MinValue;
            private Queue<string> _debugQueue = new Queue<string>();

            public SwitchBoardValues(MCUBoard box) => this.box = box;

            public List<double> ReadInputFromSwitchBoxes()
            {
                try
                {
                    string lines = box.SafeReadExisting();
                    _debugQueue.Enqueue(lines);

                    if (LastReadIsOlderThan(milliseconds: 500))
                    {
                        CALog.LogData(LogID.B, $"ReadInputFromSwitchBoxes: '{string.Join("§", _debugQueue)}'{Environment.NewLine}");
                        _debugQueue.Clear();
                    }

                    while (_debugQueue.Count > 10)
                    {
                        _debugQueue.Dequeue();
                    }

                    Match match = _switchBoxCurrentsRegex.Match(lines);
                    if (match.Success && match.Groups.Count > 4)
                    {
                        _lastValidRead = match;
                        _lastValidReadTime = DateTime.UtcNow;
                    }
                    else
                    {
                        if (_lastValidRead == null)
                            return new List<double>();

                        if (!LastReadIsOlderThan(milliseconds: 2000))
                            match = _lastValidRead;
                    }

                    if (match.Success && match.Groups.Count > 4)
                    {
                        return match.Groups.Cast<Group>().Skip(1)
                            .Select(x => double.Parse(x.Value, CultureInfo.InvariantCulture)).ToList();
                    }
                }
                catch (Exception ex)
                {
                    CALog.LogException(LogID.B, ex);
                }

                return new List<double>();  // empty list
            }

            private bool LastReadIsOlderThan(int milliseconds) =>
                _lastValidReadTime > DateTime.MinValue && DateTime.UtcNow.Subtract(_lastValidReadTime).TotalMilliseconds > milliseconds;
        }
    }
}
