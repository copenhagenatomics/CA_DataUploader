using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace CA_DataUploaderLib
{
    public static class SwitchBoardBase
    {
        private static readonly ConcurrentDictionary<MCUBoard, SwitchBoardValues> Values = new ConcurrentDictionary<MCUBoard, SwitchBoardValues>();
        public static (double[] currents, bool[] states, double temperature) ReadInputFromSwitchBoxes(MCUBoard box)
        {
            if (box == null)
                return (new double[0], new bool[0], 10000);

            var values = Values.GetOrAdd(box, b => new SwitchBoardValues(b));
            lock (values)
                return values.ReadInputFromSwitchBoxes();
        }

        private class SwitchBoardValues
        {
            private const string _SwitchBoxPattern = "P1=(-?\\d\\.\\d\\d)A P2=(-?\\d\\.\\d\\d)A P3=(-?\\d\\.\\d\\d)A P4=(-?\\d\\.\\d\\d)A(?:([01]), ([01]), ([01]), ([01])(?:, (-?\\d+.\\d\\d))?)?";
            private static readonly Regex _switchBoxCurrentsRegex = new Regex(_SwitchBoxPattern);
            private readonly MCUBoard box;
            private (double[] currents, bool[] states, double temperature) _lastRead = (new double[0], new bool[0], 10000);
            private (double[] currents, bool[] states, double temperature) _lastValidRead = (new double[0], new bool[0], 10000);
            private Stopwatch _timeSinceLastRead = new Stopwatch();
            private Stopwatch _timeSinceLastValidRead = new Stopwatch();
            private DateTime _lastValidReadTime = DateTime.MinValue;
            private Queue<string> _debugQueue = new Queue<string>();

            public SwitchBoardValues(MCUBoard box) => this.box = box;

            public (double[] currents, bool[] states, double temperature) ReadInputFromSwitchBoxes()
            {
                if (_timeSinceLastRead.IsRunning && _timeSinceLastRead.ElapsedMilliseconds < 100)
                    return _lastRead; // avoid double reads, typically from both the heatingcontroller and valvecontroller

                try
                {
                    string lines = box.SafeReadExisting();
                    _timeSinceLastRead.Restart();
                    TrackAndPrintLast10LinesOn300MsWithoutValidResponse(lines);

                    if (SwitchBoardResponseParser.TryParse(lines, out var values))
                    {
                        _timeSinceLastValidRead.Restart();
                        return _lastRead = _lastValidRead = values;
                    }
                    
                    CALog.LogData(LogID.B, $"board {box.ToString()} without valid reads since {_timeSinceLastValidRead.ElapsedMilliseconds} ms - latest invalid response: {lines}");
                    if (_timeSinceLastValidRead.IsRunning && _timeSinceLastValidRead.ElapsedMilliseconds < 300)
                        return _lastRead = _lastValidRead;
                }
                catch (Exception ex)
                {
                    CALog.LogException(LogID.B, ex);
                }

                return _lastRead = (new double[0], new bool[0], 10000);  // empty list
            }

            private void TrackAndPrintLast10LinesOn300MsWithoutValidResponse(string lines)
            {
                _debugQueue.Enqueue(lines);

                if (_timeSinceLastValidRead.IsRunning && _timeSinceLastValidRead.ElapsedMilliseconds > 300)
                {
                    CALog.LogData(LogID.B, $"ReadInputFromSwitchBoxes - no current reads in 300ms - last 10 board responses: '{string.Join("§", _debugQueue)}'{Environment.NewLine}");
                    _debugQueue.Clear();
                }

                while (_debugQueue.Count > 10)
                    _debugQueue.Dequeue();
            }
        }
    }
}
