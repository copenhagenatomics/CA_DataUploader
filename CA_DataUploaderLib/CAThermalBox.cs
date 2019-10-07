using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Globalization;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace CA_DataUploaderLib
{
    public class CAThermalBox : IDisposable
    {
        private List<MCUBoard> _mcuBoards;
        private const int TEMPERATURE_FAULT = 10000;
        private bool _running = true;
        public int FilterLength { get; set; }
        public double Frequency { get; private set; }
        private ConcurrentDictionary<string, TermoSensor> _temperatures = new ConcurrentDictionary<string, TermoSensor>();
        private Dictionary<string, Queue<double>> _filterQueue = new Dictionary<string, Queue<double>>();
        private Queue<double> _frequency = new Queue<double>();
        private List<HeaterElement> heaters = new List<HeaterElement>();

        private List<List<string>> _config = IOconf.GetInTypeK().Where(x => x[2] == "hub16").ToList();

        public bool Initialized { get; private set; }

        /// <summary>
        /// Constructor: 
        /// </summary>
        /// <param name="boards">Input a number of boards with temperature sensors </param>
        /// <param name="filterLength">1 = not filtering, larger than 1 = filtering and removing 10000 errors. </param>
        public CAThermalBox(List<MCUBoard> boards, int filterLength = 1)
        {
            Initialized = false;
            FilterLength = filterLength;
            _mcuBoards = boards.OrderBy(x => x.serialNumber).ToList();

            if (IOconf.GetOutputLevel() == LogLevel.Debug)
                ShowConfig();

            if (_config.Any())
                new Thread(() => this.LoopForever()).Start();
            else
                CALog.LogErrorAndConsole(LogID.A, "Type K thermocouple config information is missing in IO.conf");
        }

        public TermoSensor GetValue(string sensorID)
        {
            if (!_config.Any(x => x[3] == sensorID))
                throw new Exception(sensorID + " sensorID not found in _config, count: " + _config.Count());

            if (!_temperatures.ContainsKey(sensorID))
                throw new Exception(sensorID + " sensorID not found in _temperatures, count: " + _temperatures.Count());

            return _temperatures[sensorID];
        }

        public TermoSensor GetValueByTitle(string title)
        {
            if (!_config.Any(x => x[1] == title))
                throw new Exception(title + " not found in _config, count: " + _config.Count());

            var temp = _temperatures.Values.SingleOrDefault(x => x.Name == title);
            if (temp == null)
                throw new Exception(title + " not found in _temperatures, count: " + _temperatures.Count());

            return temp;
        }

        public IEnumerable<TermoSensor> GetAllValidTemperatures()
        {
            var removeBefore = DateTime.UtcNow.AddSeconds(-2);
            var timedOutSensors = _temperatures.Where(x => x.Value.TimeStamp < removeBefore).Select(x => x.Value).ToList();
            timedOutSensors.ForEach(x => x.Temperature = (x.Temperature < 10000 ? 10009 : x.Temperature)); // 10009 means timedout
            return _temperatures.Values.OrderBy(x => x.ID);
        }

        public VectorDescription GetVectorDescription()
        {
            var list = _config.Select(x => new VectorDescriptionItem("double", x[1], DataTypeEnum.Input)).ToList();
            return new VectorDescription(list, RpiVersion.GetHardware(), RpiVersion.GetSoftware());
        }

        private void LoopForever()
        {
            List<double> numbers = new List<double>();
            List<string> values = new List<string>();
            string row = string.Empty;
            int badRow = 0;

            var logLevel = IOconf.GetOutputLevel();
            while (_running)
            {
                try
                {
                    int hubID = 0;
                    foreach (var board in _mcuBoards)
                    {
                        row = board.ReadLine();
                        values = row.Split(",".ToCharArray()).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                        numbers = values.Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToList();
                        if (numbers.Count == 18) // old model. 
                        {
                            hubID = (int)numbers[0];
                            ProcessLine(numbers.Skip(1), hubID++, board);
                        }
                        else
                            ProcessLine(numbers, hubID++, board);

                        if (logLevel == LogLevel.Debug)
                            CALog.LogData(LogID.A, MakeDebugString(row) + Environment.NewLine);
                    }
                    badRow = 0;
                    Initialized = true;
                }
                catch (Exception ex)
                {
                    if (logLevel == LogLevel.Debug)
                    {
                        CALog.LogInfoAndConsoleLn(LogID.A, "Exception at: " + row);
                        values.ForEach(x => CALog.LogInfoAndConsoleLn(LogID.A, x));
                        numbers.ForEach(x => CALog.LogInfoAndConsoleLn(LogID.A, x.ToString()));
                        CALog.LogException(LogID.A, ex);
                    }

                    CALog.LogInfoAndConsoleLn(LogID.A, ".");
                    badRow++;
                    if (badRow > 10)
                    {
                        CALog.LogInfoAndConsoleLn(LogID.A, "Too many bad rows from thermocouple ports.. shutting down.");
                        CALog.LogException(LogID.A, ex);
                        _running = false;
                    }
                }
            }

            foreach (var board in _mcuBoards)
                board.Close();
        }

        private void ProcessLine(IEnumerable<double> numbers, int hubID, MCUBoard board)
        {
            var timestamp = DateTime.UtcNow;

            int i = 0;
            foreach (var value in numbers)
            {
                var sensor = GetSensor(hubID, i);
                if (sensor.row != null)
                {
                    if (_temperatures.ContainsKey(sensor.key))
                    {
                        if (sensor.readJunction)
                        {
                            _temperatures[sensor.key].Junction = value;
                        }
                        else
                        {
                            Frequency = FrequencyLowPassFilter(timestamp.Subtract(_temperatures[sensor.key].TimeStamp));
                            _temperatures[sensor.key].TimeStamp = timestamp;
                            _temperatures[sensor.key].Temperature = (FilterLength > 1) ? LowPassFilter(value, sensor.key) : value;
                        }
                    }
                    else
                    {
                        Debug.Assert(sensor.readJunction == false);
                        _temperatures.TryAdd(sensor.key, new TermoSensor(_config.IndexOf(sensor.row), sensor.row[1], GetHeater(sensor.row)) { Temperature = value, TimeStamp = timestamp, Key = sensor.key, Board = board });
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

        private HeaterElement GetHeater(List<string> row)
        {
            if (row.Count <= 4)
                return null;

            var list = row[4].Split(".".ToCharArray()).ToList();
            int port;
            if (!int.TryParse(list[1], out port))
            {
                CALog.LogInfoAndConsoleLn(LogID.A, $"Unable to parse heating element info in IO.conf ({string.Join(",", row)})");
                return null;
            }

            var he = heaters.SingleOrDefault(x => x.SwitchBoard == list[0] && x.port == port);
            if (he == null)
            {
                he = new HeaterElement { SwitchBoard = list[0], port = port };
                heaters.Add(he);
            }

            return he;
        }

        private (string key, List<string> row, bool readJunction) GetSensor(int hubID, int i)
        {
            string key = hubID.ToString() + "." + i.ToString();
            return (key, _config.SingleOrDefault(x => x[3] == key), i>17);
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

            while (_frequency.Count() > _temperatures.Count() * 10)
            {
                _frequency.Dequeue();
            }

            return _frequency.Average();
        }

        private string MakeDebugString(string row)
        {
            string filteredValues = string.Join(", ", GetAllValidTemperatures().Select(x => x.Temperature.ToString("N2").PadLeft(8)));
            return row.Replace("\n", "").PadRight(120).Replace("\r", "Freq=" + Frequency.ToString("N1")) + filteredValues;
        }

        private void ShowConfig()
        {
            foreach (var x in _config)
            {
                foreach (var y in x)
                    CALog.LogInfoAndConsole(LogID.A, y + ",");

                CALog.LogInfoAndConsoleLn(LogID.A, "");
            }

            CALog.LogInfoAndConsoleLn(LogID.A, FilterLength.ToString());
        }

        public void Dispose()
        {
            _running = false;
            for (int i = 0; i < 100; i++)
            {
                foreach(var board in _mcuBoards)
                    if (board.IsOpen)
                            Thread.Sleep(10);
            }

            foreach (var board in _mcuBoards)
                ((IDisposable)board).Dispose();
        }
    }
}
