using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Globalization;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using CA_DataUploaderLib.IOconf;

namespace CA_DataUploaderLib
{
    public class BaseSensorBox : IDisposable
    {
        protected bool _running = true;
        public int FilterLength { get; set; }
        public double Frequency { get; private set; }
        public string Title { get; protected set; }

        protected CALogLevel _logLevel = IOconfFile.GetOutputLevel();
        protected CommandHandler _cmdHandler;
        protected ConcurrentDictionary<IOconfInput, SensorSample> _values = new ConcurrentDictionary<IOconfInput, SensorSample>();
        private Dictionary<IOconfInput, Queue<double>> _filterQueue = new Dictionary<IOconfInput, Queue<double>>();
        private Queue<double> _frequency = new Queue<double>();
        private List<HeaterElement> heaters = new List<HeaterElement>();

        protected List<IOconfInput> _config;

        public bool Initialized { get; protected set; }

        public BaseSensorBox() { }

        public SensorSample GetValue(string sensorKey)
        {
            if(!_values.Any(x => (x.Key.BoxName + x.Key.PortNumber.ToString()) == sensorKey))
                throw new Exception(sensorKey + " not found in _values, count: " + _values.Count());
            return _values.Single(x => (x.Key.BoxName + x.Key.PortNumber.ToString()) == sensorKey).Value;
        }

        public SensorSample GetValueByTitle(string title)
        {
            if (!_config.Any(x => x.Name == title))
                throw new Exception(title + " not found in _config. Known names: " + string.Join(", ", _config.Select(x => x.Name)));

            var temp = _values.Values.SingleOrDefault(x => x.Name == title);
            if (temp == null)
                throw new Exception(title + " not found in _values, count: " + _values.Count());

            return temp;
        }

        public IEnumerable<SensorSample> GetAllValidDatapoints()
        {
            var removeBefore = DateTime.UtcNow.AddSeconds(-2);
            var timedOutSensors = _values.Where(x => x.Value.TimeStamp < removeBefore).Select(x => x.Value).ToList();
            if(!Debugger.IsAttached)
                timedOutSensors.ForEach(x => x.Value = (x.Value < 10000 ? 10009 : x.Value)); // 10009 means timedout

            return _config.Where(x => _values.ContainsKey(x)).Select(x => _values[x]);
        }

        public virtual List<VectorDescriptionItem> GetVectorDescriptionItems()
        {
            return _config.Select(x => new VectorDescriptionItem("double", x.Name, DataTypeEnum.Input)).ToList();
        }

        protected bool ShowQueue(List<string> args)
        {
            var sb = new StringBuilder();
            foreach (var t in _values)
            {
                sb.Append($"{t.Value.Name.PadRight(22)}={t.Value.Value.ToString("N2").PadLeft(9)}  ");
                if(_filterQueue.ContainsKey(t.Key))
                    _filterQueue[t.Key].ToList().ForEach(x => sb.Append(", " + x.ToString("N2").PadLeft(9)));
                sb.Append(Environment.NewLine);
            }

            CALog.LogInfoAndConsoleLn(LogID.A, sb.ToString());
            return true;
        }

        protected void LoopForever()
        {
            List<double> numbers = new List<double>();
            List<string> values = new List<string>();
            DateTime start = DateTime.Now;
            string row = string.Empty;
            int badRow = 0;
            List<string> badPorts = new List<string>();
            MCUBoard exBoard = null;

            while (_running)
            {
                try
                {
                    foreach (var board in Boards())
                    {
                        exBoard = board; // only used in exception
                        values.Clear();
                        numbers.Clear();
                        row = board.SafeReadLine();
                        values = row.Split(",".ToCharArray()).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                        numbers = values.Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToList();
                        ProcessLine(numbers, board);

                        if (_logLevel == CALogLevel.Debug)
                            CALog.LogData(LogID.A, MakeDebugString(row) + Environment.NewLine);

                    }

                    Initialized = true;
                }
                catch (Exception ex)
                {
                    if (_logLevel == CALogLevel.Debug)
                    {
                        CALog.LogInfoAndConsoleLn(LogID.A, "Exception at: " + row);
                        CALog.LogInfoAndConsole(LogID.A, "Values: ");
                        values.ForEach(x => CALog.LogInfoAndConsoleLn(LogID.A, x));
                        numbers.ForEach(x => CALog.LogInfoAndConsoleLn(LogID.A, x.ToString()));
                        CALog.LogException(LogID.A, ex);
                    }

                    CALog.LogInfoAndConsoleLn(LogID.A, ".");
                    if(exBoard != null)
                        badPorts.Add($"{exBoard.PortName}:{exBoard.serialNumber} = {row}");

                    badRow++;
                    if (badRow > 10)
                    {
                        CALog.LogInfoAndConsoleLn(LogID.A, "Too many bad rows from thermocouple ports.. shutting down:");
                        badPorts.ForEach(x => CALog.LogInfoAndConsoleLn(LogID.A, x));
                        CALog.LogException(LogID.A, ex);
                        if(_cmdHandler != null)
                            _cmdHandler.Execute("escape");

                        _running = false;
                    }
                }
            }

            foreach (var board in Boards())
            {
                if(board != null)
                    board.SafeClose();
            }

            CALog.LogInfoAndConsoleLn(LogID.A, $"Exiting {Title}.LoopForever() " + DateTime.Now.Subtract(start).TotalSeconds.ToString() + " seconds");
        }

        private IEnumerable<MCUBoard> Boards()
        {
            return _config.Select(x => x.Map.Board).Distinct();
        }
        protected bool Stop(List<string> args)
        {
            _running = false;
            return true;
        }

        private void ProcessLine(IEnumerable<double> numbers, MCUBoard board)
        {
            var timestamp = DateTime.UtcNow;

            int i = 0;
            foreach (var value in numbers)
            {
                var sensor = _config.SingleOrDefault(x => x.BoxName == board.BoxName && x.PortNumber == i);
                if (sensor != null)
                {
                    if (_values.ContainsKey(sensor))
                    {
                        Frequency = FrequencyLowPassFilter(timestamp.Subtract(_values[sensor].TimeStamp));
                        _values[sensor].TimeStamp = timestamp;
                        _values[sensor].Value = (FilterLength > 1) ? LowPassFilter(value, sensor) : value;
                    }
                    else
                    {
                        var numberOfPorts = numbers.Count() == 11 ? "1x10" : "2x8";
                        _values.TryAdd(sensor, new SensorSample(sensor) 
                                                            { 
                                                                Value = value, 
                                                                TimeStamp = timestamp, 
                                                                NumberOfPorts = numberOfPorts,
                                                                HubID = GetHubID(sensor.BoxName),
                                                                SerialNumber = board.serialNumber
                                                            });
                        if (FilterLength > 1)
                        {
                            _filterQueue.Add(sensor, new Queue<double>());
                            _filterQueue[sensor].Enqueue(value);
                        }
                    }

                    HandleSaltLeakage(sensor);
                }

                i++;
            }
        }

        private void HandleSaltLeakage(IOconfInput sensor)
        {
            if (sensor.GetType() == typeof(IOconfSaltLeakage))
            {
                if (_values[sensor].Value < 3000 && _values[sensor].Value > 0)  // Salt leakage algorithm. 
                {
                    CALog.LogErrorAndConsole(LogID.A, $"Salt leak detected from {sensor.Name}={_values[sensor].Value} {DateTime.Now.ToString("dd-MMM-yyyy HH:mm")}");
                    _values[sensor].Value = 1d;
                    if (_cmdHandler != null)
                        _cmdHandler.Execute("escape"); // make the whole system shut down. 
                }
                else
                {
                    _values[sensor].Value = 0d; // no leakage
                }
            }
        }

        private int GetHubID(string ioconfName)
        {
            return _config.GroupBy(x => x.BoxName).Select(x => x.Key).ToList().IndexOf(ioconfName);
        }

        private double LowPassFilter(double value, IOconfInput sensor)
        {
            double result = value;
            _filterQueue[sensor].Enqueue(value);
            var goodValues = _filterQueue[sensor].Where(x => x < 10000 && x != 0);
            if (goodValues.Any())
                result = goodValues.Average();

            while (_filterQueue[sensor].Count() > FilterLength)
            {
                _filterQueue[sensor].Dequeue();
            }

            return result;
        }

        private double FrequencyLowPassFilter(TimeSpan ts)
        {
            _frequency.Enqueue(1.0/ts.TotalSeconds);

            while (_frequency.Count() > _values.Count() * 10)
            {
                _frequency.Dequeue();
            }

            return _frequency.Average();
        }

        private string MakeDebugString(string row)
        {
            string filteredValues = string.Join(", ", GetAllValidDatapoints().Select(x => x.Value.ToString("N2").PadLeft(8)));
            return row.Replace("\n", "").PadRight(120).Replace("\r", "Freq=" + Frequency.ToString("N1")) + filteredValues;
        }

        public void Dispose()
        {
            _running = false;
            for (int i = 0; i < 100; i++)
            {
                foreach(var board in Boards())
                    if (board != null && board.IsOpen)
                            Thread.Sleep(10);
            }

            foreach (var board in Boards())
                if(board != null)
                    ((IDisposable)board).Dispose();
        }
    }
}
