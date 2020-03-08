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
        protected ConcurrentDictionary<string, SensorSample> _values = new ConcurrentDictionary<string, SensorSample>();
        private Dictionary<string, Queue<double>> _filterQueue = new Dictionary<string, Queue<double>>();
        private Queue<double> _frequency = new Queue<double>();
        private List<HeaterElement> heaters = new List<HeaterElement>();

        protected List<IOconfInput> _config;

        public bool Initialized { get; protected set; }

        public BaseSensorBox() { }

        public SensorSample GetValue(int sensorID)
        {
            if (!_config.Any(x => x.PortNumber == sensorID))
                throw new Exception(sensorID + " sensorID not found in _config, count: " + _config.Count());

            if (!_values.ContainsKey(sensorID.ToString()))
                throw new Exception(sensorID + " sensorID not found in _temperatures, count: " + _values.Count());

            return _values[sensorID.ToString()];
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

            return _values.Values.OrderBy(x => x.ID);
        }

        public virtual List<VectorDescriptionItem> GetVectorDescriptionItems()
        {
            return _config.Select(x => new VectorDescriptionItem("double", x.Name, DataTypeEnum.Input)).ToList();
        }

        protected bool ShowQueue(List<string> args)
        {
            var sb = new StringBuilder();
            foreach (var t in _values.OrderBy(x => x.Key).Select(x => x.Value))
            {
                sb.Append($"{t.Name.PadRight(22)}={t.Value.ToString("N2").PadLeft(9)}  ");
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

                        if (numbers.Count == 18 && board.productType.StartsWith("Temperature")) // old model. 
                        {
                            int hubID = (int)numbers[0];
                            ProcessLine(numbers.Skip(1), hubID.ToString(), board);
                        }
                        else
                            ProcessLine(numbers, board.BoxName, board);

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

        private void ProcessLine(IEnumerable<double> numbers, string IOconfName, MCUBoard board)
        {
            var timestamp = DateTime.UtcNow;

            int i = 0;
            foreach (var value in numbers)
            {
                var sensor = GetSensor(IOconfName, i);
                if (sensor.input != null)
                {
                    if (_values.ContainsKey(sensor.key))
                    {
                        if (sensor.readJunction)
                        {
                            _values[sensor.key].Reference = value;
                        }
                        else
                        {
                            Frequency = FrequencyLowPassFilter(timestamp.Subtract(_values[sensor.key].TimeStamp));
                            _values[sensor.key].TimeStamp = timestamp;
                            _values[sensor.key].Value = (FilterLength > 1) ? LowPassFilter(value, sensor.key) : value;
                        }
                    }
                    else
                    {
                        Debug.Assert(sensor.readJunction == false);
                        var numberOfPorts = numbers.Count() == 11 ? "1x10" : "2x8";
                        _values.TryAdd(sensor.key, new SensorSample(_config.IndexOf(sensor.input), sensor.key, sensor.input.Name, board) { Value = value, TimeStamp = timestamp, hubID = GetHubID(sensor.input.BoxName), NumberOfPorts = numberOfPorts });
                        if (FilterLength > 1)
                        {
                            _filterQueue.Add(sensor.key, new Queue<double>());
                            _filterQueue[sensor.key].Enqueue(value);
                        }
                    }
                }

                i++;
            }
        }

        private int GetHubID(string ioconfName)
        {
            return _config.GroupBy(x => x.BoxName).Select(x => x.Key).ToList().IndexOf(ioconfName);
        }

        private (string key, IOconfInput input, bool readJunction) GetSensor(string IOconfName, int i)
        {
            string key = IOconfName + "." + i.ToString();
            return (key, _config.SingleOrDefault(x => x.BoxName == IOconfName && x.PortNumber == i), i>17);
        }

        private double LowPassFilter(double value, string key)
        {
            double result = value;
            _filterQueue[key].Enqueue(value);
            var goodValues = _filterQueue[key].Where(x => x < 10000 && x != 0);
            if (goodValues.Any())
                result = goodValues.Average();

            while (_filterQueue[key].Count() > FilterLength)
            {
                _filterQueue[key].Dequeue();
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
