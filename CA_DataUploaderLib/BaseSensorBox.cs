﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Globalization;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace CA_DataUploaderLib
{
    public class BaseSensorBox : IDisposable
    {
        protected List<MCUBoard> _mcuBoards;
        private const int TEMPERATURE_FAULT = 10000;
        protected bool _running = true;
        public int FilterLength { get; set; }
        public double Frequency { get; private set; }

        protected string _title = "CAThermalBox";
        protected CALogLevel _logLevel = IOconf.GetOutputLevel();
        protected CommandHandler _cmdHandler;
        protected ConcurrentDictionary<string, SensorSample> _values = new ConcurrentDictionary<string, SensorSample>();
        private Dictionary<string, Queue<double>> _filterQueue = new Dictionary<string, Queue<double>>();
        private Queue<double> _frequency = new Queue<double>();
        private List<HeaterElement> heaters = new List<HeaterElement>();

        protected List<List<string>> _config;

        public bool Initialized { get; protected set; }

        public BaseSensorBox() { }

        /// <summary>
        /// Constructor: 
        /// </summary>
        /// <param name="boards">Input a number of boards with temperature sensors </param>
        /// <param name="filterLength">1 = not filtering, larger than 1 = filtering and removing 10000 errors. </param>
        public BaseSensorBox(List<MCUBoard> boards, CommandHandler cmd = null, int filterLength = 1)
        {
            Initialized = false;
            FilterLength = filterLength;
            _mcuBoards = boards.OrderBy(x => x.serialNumber).ToList();
            _config = IOconf.GetInTypeK().ToList();
            _cmdHandler = cmd;
            if (cmd != null)
            {
                cmd.AddCommand("Temperatures", ShowQueue);
                cmd.AddCommand("help", HelpMenu);
            }

            if (_logLevel == CALogLevel.Debug)
                ShowConfig();

            if (_config.Any())
                new Thread(() => this.LoopForever()).Start();
            else
                CALog.LogErrorAndConsole(LogID.A, "Type K thermocouple config information is missing in IO.conf");
        }

        public SensorSample GetValue(string sensorID)
        {
            if (!_config.Any(x => x[3] == sensorID))
                throw new Exception(sensorID + " sensorID not found in _config, count: " + _config.Count());

            if (!_values.ContainsKey(sensorID))
                throw new Exception(sensorID + " sensorID not found in _temperatures, count: " + _values.Count());

            return _values[sensorID];
        }

        public SensorSample GetValueByTitle(string title)
        {
            if (!_config.Any(x => x[1] == title))
                throw new Exception(title + " not found in _config. Known names: " + string.Join(", ", _config.Select(x => x[1])));

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

        public VectorDescription GetVectorDescription()
        {
            var list = _config.Select(x => new VectorDescriptionItem("double", x[1], DataTypeEnum.Input)).ToList();
            CALog.LogInfoAndConsoleLn(LogID.A, $"{list.Count.ToString().PadLeft(2)} datapoints from {_title}");
            return new VectorDescription(list, RpiVersion.GetHardware(), RpiVersion.GetSoftware());
        }

        private bool HelpMenu(List<string> args)
        {
            CALog.LogInfoAndConsoleLn(LogID.A, $"temperatures              - show all temperatures in input queue");
            return true;
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

            while (_running)
            {
                try
                {
                    int hubID = 0;
                    foreach (var board in _mcuBoards)
                    {
                        row = board.SafeReadLine();
                        values = row.Split(",".ToCharArray()).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                        numbers = values.Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToList();

                        if (numbers.Count == 18 && board.productType.StartsWith("Temperature")) // old model. 
                        {
                            hubID = (int)numbers[0];
                            ProcessLine(numbers.Skip(1), hubID++, board);
                        }
                        else
                            ProcessLine(numbers, hubID++, board);

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
                board.SafeClose();

            CALog.LogInfoAndConsoleLn(LogID.A, $"Exiting {_title}.LoopForever() " + DateTime.Now.Subtract(start).TotalSeconds.ToString() + " seconds");
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
                        _values.TryAdd(sensor.key, new SensorSample(_config.IndexOf(sensor.row), sensor.row[1], GetHeater(sensor.row)) { Value = value, TimeStamp = timestamp, Key = sensor.key, Board = board });
                        if (FilterLength > 1)
                        {
                            _filterQueue.Add(sensor.key, new Queue<double>());
                            _filterQueue[sensor.key].Enqueue(value);
                        }
                    }
                }
                else
                {
                    CALog.LogData(LogID.A, sensor.key + " not found IO.conf file" + Environment.NewLine);
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

        protected void ShowConfig()
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