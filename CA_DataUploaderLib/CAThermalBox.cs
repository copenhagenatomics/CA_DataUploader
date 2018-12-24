using System;
using System.IO.Ports;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Globalization;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace CA_DataUploaderLib
{
    public class CAThermalPort
    {
        public SerialPort port;
        public MCUBoard board;

        public CAThermalPort(MCUBoard input)
        {
            board = input;
            port = new SerialPort(board.portName, board.baudRate);
            port.Open();
        }
        public string ReadLine()
        {
            return port.ReadLine();
        }
    }

    public class CAThermalBox : IDisposable
    {
        private List<CAThermalPort> _serialPorts = new List<CAThermalPort>();
        private const int TEMPERATURE_FAULT = 10000;
        private bool _running = true;
        private bool _junction = false;
        public int FilterLength { get; set; }
        public double Frequency { get; private set; }
        private ConcurrentDictionary<string, TermoSensor> _temperatures = new ConcurrentDictionary<string, TermoSensor>();
        private Dictionary<string, Queue<double>> _filterQueue = new Dictionary<string, Queue<double>>();
        private Queue<double> _frequency = new Queue<double>();

        private List<List<string>> _config = IOconf.GetInTypeK().Where(x => x[2] == "hub16").ToList();

        public bool Initialized { get; private set; }

        public CAThermalBox(List<MCUBoard> boards, int filterLength = 1, bool junction = false)
        {
            Initialized = false;
            _junction = junction;
            FilterLength = filterLength;
            foreach (var board in boards.OrderBy(x => x.serialNumber))
            {
                _serialPorts.Add(new CAThermalPort(board));
                if (!_serialPorts.Last().port.IsOpen)
                {
                    throw new Exception($"Unable to open Serial port {board.portName}");
                }

                if (IOconf.GetOutputLevel() == LogLevel.Normal)
                {
                    for (int i = 0; i < 2; i++)
                        Console.WriteLine(_serialPorts.Last().ReadLine());
                }
            }

            if(IOconf.GetOutputLevel() == LogLevel.Debug)
                ShowConfig();

            new Thread(() => this.LoopForever()).Start();

            //_serialPort.WriteLine("Serial");
            //Thread.Sleep(100);
            //_serialPort.WriteLine("");
        }

        public TermoSensor GetValue(string sensorID)
        {
            if (!_config.Any(x => x[3] == sensorID))
                throw new Exception(sensorID + " sensorID not found in _config, count: " + _config.Count());

            if (!_temperatures.ContainsKey(sensorID))
                throw new Exception(sensorID + " sensorID not found in _temperatures, count: " + _temperatures.Count());

            return _temperatures[sensorID];
        }

        public IEnumerable<TermoSensor> GetAllValidTemperatures()
        {
            if (IOconf.GetOutputLevel() == LogLevel.Debug)
                Console.WriteLine("All temperatures: " + _temperatures.Count());

            var removeBefore = DateTime.UtcNow.AddSeconds(-2);
            var list = _temperatures.Where(x => x.Value.TimeStamp > removeBefore).Select(x => x.Key).ToList();
            return _temperatures.Where(x => list.Contains(x.Key)).Select(x => x.Value).OrderBy(x => x.ID);
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
                    foreach (var port in _serialPorts)
                    {
                        row = port.ReadLine();
                        if (logLevel == LogLevel.Debug)
                            Console.WriteLine(row);

                        Debug.Print(row);

                        values = row.Split(",".ToCharArray()).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                        numbers = values.Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToList();
                        if (numbers.Count == 18) // old model. 
                        {
                            hubID = (int)numbers[0];
                            ProcessLine(numbers.Skip(1), hubID, port.board);
                        }
                        else
                            ProcessLine(numbers, ++hubID, port.board);
                    }
                    badRow = 0;
                    Initialized = true;
                }
                catch (Exception ex)
                {
                    if (logLevel == LogLevel.Debug)
                    {
                        Console.WriteLine("Exception at: " + row);
                        values.ForEach(x => Console.WriteLine(x));
                        numbers.ForEach(x => Console.WriteLine(x));
                        Console.WriteLine(ex.ToString());
                    }

                    Console.Write('.');
                    badRow++;
                    if (badRow > 10)
                        _running = false;
                }
            }

            foreach (var board in _serialPorts)
                board.port.Close();
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
                        _temperatures.TryAdd(sensor.key, new TermoSensor(_config.IndexOf(sensor.row), sensor.row[1]) { Temperature = value, TimeStamp = timestamp, Key = sensor.key, board = board });
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

        private (string key, List<string> row, bool readJunction) GetSensor(int hubID, int i)
        {
            string key = hubID.ToString() + "." + i.ToString();
            if (_junction && i > 17)
            {
                key = hubID.ToString() + "." + (i % 10).ToString();
            }

            return (key, _config.SingleOrDefault(x => x[3] == key), i>17);
        }

        private double LowPassFilter(double value, string key)
        {
            double result = value;
            _filterQueue[key].Enqueue(value);
            var goodValues = _filterQueue[key].Where(x => x < 10000);
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

        private void ShowConfig()
        {
            foreach (var x in _config)
            {
                foreach (var y in x)
                    Console.Write(y + ",");

                Console.WriteLine();
            }
        }

        public void Dispose()
        {
            _running = false;
            for (int i = 0; i < 100; i++)
            {
                foreach(var board in _serialPorts)
                    if (board.port.IsOpen)
                            Thread.Sleep(10);
            }

            foreach (var board in _serialPorts)
                ((IDisposable)board.port).Dispose();
        }
    }
}
