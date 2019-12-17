using System;
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
        protected bool _running = true;
        public int FilterLength { get; set; }
        public double Frequency { get; private set; }
        public string Title { get; protected set; }

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
            Title = "Thermocouples";
            Initialized = false;
            FilterLength = filterLength;
            _mcuBoards = boards.OrderBy(x => x.serialNumber).ToList();
            _config = IOconf.GetTypeK().ToList();
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

        public virtual List<VectorDescriptionItem> GetVectorDescriptionItems()
        {
            return _config.Select(x => new VectorDescriptionItem("double", x[1], DataTypeEnum.Input)).ToList();
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
                    foreach (var board in _mcuBoards)
                    {
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
                            ProcessLine(numbers, board.IOconfName, board);

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
                    badRow++;
                    if (badRow > 10)
                    {
                        CALog.LogInfoAndConsoleLn(LogID.A, "Too many bad rows from thermocouple ports.. shutting down.");
                        CALog.LogException(LogID.A, ex);
                        _cmdHandler.Execute("escape");
                        _running = false;
                    }
                }
            }

            foreach (var board in _mcuBoards)
                board.SafeClose();

            CALog.LogInfoAndConsoleLn(LogID.A, $"Exiting {Title}.LoopForever() " + DateTime.Now.Subtract(start).TotalSeconds.ToString() + " seconds");
        }

        private void ProcessLine(IEnumerable<double> numbers, string IOconfName, MCUBoard board)
        {
            var timestamp = DateTime.UtcNow;

            int i = 0;
            foreach (var value in numbers)
            {
                var sensor = GetSensor(IOconfName, i);
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
                        var numberOfPorts = numbers.Count() == 11? "1x10":"2x8";
                        _values.TryAdd(sensor.key, new SensorSample(_config.IndexOf(sensor.row), sensor.key, sensor.row[1], board, GetHeater(sensor.row)) { Value = value, TimeStamp = timestamp, hubID = GetHubID(sensor.row[2]), NumberOfPorts= numberOfPorts });
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
            return _config.GroupBy(x => x[2]).Select(x => x.Key).ToList().IndexOf(ioconfName);
        }

        private HeaterElement GetHeater(List<string> row)
        {
            if (row.Count <= 4)
                return null;

            var relay = IOconf.GetOut230Vac(row[4]);
            var he = heaters.SingleOrDefault(x => x.USBPort == relay.USBPort && x.PortNumber == relay.PortNumber);
            if (he == null && relay.USBPort != "unknown")
            {
                he = new HeaterElement { USBPort = relay.USBPort, PortNumber = relay.PortNumber, Name = relay.Name };
                heaters.Add(he);
            }

            return he;
        }

        private (string key, List<string> row, bool readJunction) GetSensor(string IOconfName, int i)
        {
            string key = IOconfName + "." + i.ToString();
            return (key, _config.SingleOrDefault(x => x[2] == IOconfName && x[3] == i.ToString()), i>17);
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
