using System;
using System.IO.Ports;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Globalization;
using System.Collections.Concurrent;

namespace CA_DataUploaderLib
{
    public class CAThermalBox : IDisposable
    {
        private SerialPort _serialPort;
        private const int TEMPERATURE_FAULT = 10000;
        private bool _running = true;
        private int _maxNumberOfHubs;
        private int _filterLength;
        private ConcurrentDictionary<string, TermoSensor> _temperatures = new ConcurrentDictionary<string, TermoSensor>();
        private Dictionary<string, Queue<double>> _filterQueue = new Dictionary<string, Queue<double>>();

        private List<List<string>> _config = IOconf.GetInTypeK().Where(x => x[3] == "hub16").ToList();

        public bool Initialized { get; private set; }

        public CAThermalBox(string portname, int maxNumberOfHubs, int filterLength = 1)
        {
            Initialized = false;
            _maxNumberOfHubs = maxNumberOfHubs;
            _filterLength = filterLength;
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
        }

        public TermoSensor GetValue(int sensorID)
        {
            if (!_config.Any(x => x[2] == sensorID.ToString()))
                throw new Exception(sensorID + " sensorID not found in _config, count: " + _config.Count());

            if (!_temperatures.Any(x => x.Value.ID == sensorID))
                throw new Exception(sensorID + " sensorID not found in _temperatures, count: " + _temperatures.Count());

            return _temperatures.First(x => x.Value.ID == sensorID).Value;
        }

        public IEnumerable<TermoSensor> GetAllValidTemperatures()
        {
            return _temperatures.Values.Where(x => _config.Any(y => y[2] == x.ID.ToString()));
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

                    Console.WriteLine();
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
                string key = hubID.ToString() + "." + i++.ToString();
                var row = _config.SingleOrDefault(x => x[4] == key);
                if (row != null)
                {
                    if (_temperatures.ContainsKey(key))
                    {
                        _temperatures[key].TimeStamp = timestamp;
                        _temperatures[key].Temperature = (_filterLength > 1)?LowPassFilter(value, key):value;
                    }
                    else
                    {
                        _temperatures.TryAdd(key, new TermoSensor(row) { Temperature = value, TimeStamp = timestamp });
                        if (_filterLength > 1)
                        {
                            _filterQueue.Add(key, new Queue<double>());
                            _filterQueue[key].Enqueue(value);
                        }
                    }
                }
            }
        }

        private double LowPassFilter(double value, string key)
        {
            double result = value;
            _filterQueue[key].Enqueue(value);
            var goodValues = _filterQueue[key].Where(x => x < 10000);
            if (goodValues.Any())
                result = goodValues.Average();

            while (_filterQueue[key].Count() > _filterLength)
            {
                _filterQueue[key].Dequeue();
            }

            return result;
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
