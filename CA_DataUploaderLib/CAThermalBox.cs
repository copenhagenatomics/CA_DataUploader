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
    public class CAThermalBox : IDisposable
    {
        private SerialPort _serialPort;
        private const int TEMPERATURE_FAULT = 10000;
        private bool _running = true;
        private bool _junction = false;
        private int _maxNumberOfHubs;
        public int FilterLength { get; set; }
        public double Frequency { get; private set; }
        private ConcurrentDictionary<string, TermoSensor> _temperatures = new ConcurrentDictionary<string, TermoSensor>();
        private Dictionary<string, Queue<double>> _filterQueue = new Dictionary<string, Queue<double>>();
        private Queue<double> _frequency = new Queue<double>();

        private List<List<string>> _config = IOconf.GetInTypeK().Where(x => x[2] == "hub16").ToList();

        public bool Initialized { get; private set; }

        public CAThermalBox(string portname, int maxNumberOfHubs, int filterLength = 1, bool junction = false)
        {
            Initialized = false;
            _maxNumberOfHubs = maxNumberOfHubs;
            _junction = junction;
            FilterLength = filterLength;
            _serialPort = new SerialPort(portname, 115200);
            _serialPort.Open();
            if(!_serialPort.IsOpen)
            {
                throw new Exception("Unable to open Serial port");
            }

            if (IOconf.GetOutputLevel() == LogLevel.Normal)
            {
                for (int i = 0; i < 2; i++)
                    Console.WriteLine(_serialPort.ReadLine());
            }

            if(IOconf.GetOutputLevel() == LogLevel.Debug)
                ShowConfig();

            new Thread(() => this.LoopForever()).Start();

            _serialPort.WriteLine("Serial");
            Thread.Sleep(100);
            _serialPort.WriteLine("");
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
            var removeBefore = DateTime.UtcNow.AddSeconds(-2);
            var list = _temperatures.Where(x => x.Value.TimeStamp < removeBefore).Select(x => x.Key).ToList();
            TermoSensor dummy;
            list.ForEach(x => _temperatures.TryRemove(x, out dummy));
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
                    row = _serialPort.ReadLine();
                    if (logLevel == LogLevel.Debug)
                        Console.WriteLine(row);

                    Debug.Print(row);

                    values = row.Split(",".ToCharArray()).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                    numbers = values.Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToList();
                    ProcessLine(numbers);
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

            _serialPort.Close();
        }

        private void ProcessLine(List<double> numbers)
        {
            var timestamp = DateTime.UtcNow;
            var hubID = Validate(numbers);

            int i = 0;
            foreach (var value in numbers.Skip(1))
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
                        _temperatures.TryAdd(sensor.key, new TermoSensor(_config.IndexOf(sensor.row), sensor.row[1]) { Temperature = value, TimeStamp = timestamp, Key = sensor.key });
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

        private int Validate(List<double> numbers)
        {
            //if (numbers.Count != 19)
            //    throw new ArgumentException("Arduino not sending right number of values. Expected 19, received " + numbers.Count);

            int hubID = Convert.ToInt32(numbers.First());
            if (hubID != numbers.First())
                throw new Exception($"HubID was not a integer number: {hubID} != {numbers.First()}");

            if (hubID < 0 || hubID >= _maxNumberOfHubs)
                throw new Exception($"Expected hub number between 0 and {_maxNumberOfHubs}, but received {numbers.First()}");

            if (numbers[1] < -10 || numbers[1] > 100)
                throw new Exception($"Hub temperature was outside allowed range: {numbers[1]}");

            return hubID;
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
                if (_serialPort.IsOpen) Thread.Sleep(10);
            }

            ((IDisposable)_serialPort).Dispose();
        }
    }
}
