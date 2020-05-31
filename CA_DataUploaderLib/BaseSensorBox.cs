using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Globalization;
using System.Text;
using CA_DataUploaderLib.IOconf;
using System.Text.RegularExpressions;

namespace CA_DataUploaderLib
{
    public class BaseSensorBox : IDisposable
    {
        protected bool _running = true;
        private double _loopTime = 0;
        public string Title { get; protected set; }

        protected CALogLevel _logLevel = IOconfFile.GetOutputLevel();
        protected CommandHandler _cmdHandler;
        protected Dictionary<IOconfInput, SensorSample> _values = new Dictionary<IOconfInput, SensorSample>();

        protected List<IOconfInput> _config;
        protected List<MCUBoard> _boards = new List<MCUBoard>();

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

        public IEnumerable<SensorSample> GetAllDatapoints()
        {
            return _values.Values;  
        }

        public IEnumerable<double> GetFrequencyAndFilterCount()
        {
            if (_logLevel != CALogLevel.Debug)
                return new List<double>();  // empty. 

            var list1 = _values.Values.GroupBy(x => x.Input.BoxName).OrderBy(x => x.Key).Select(x => x.First().GetFrequency());
            var list2 = _values.Values.GroupBy(x => x.Input.BoxName).OrderBy(x => x.Key).Select(x => x.First().FilterCount()).ToList();
            list2.Add(_loopTime);
            return list1.Concat(list2);
        }

        public virtual List<VectorDescriptionItem> GetVectorDescriptionItems()
        {
            var list = _config.Select(x => new VectorDescriptionItem("double", x.Name, DataTypeEnum.Input)).ToList();
            // list.AddRange(_config.Select(x => new VectorDescriptionItem("double", x.Name + "_latest", DataTypeEnum.Input)).ToList());
            if (_logLevel == CALogLevel.Debug)
            {
                list.AddRange(_boards.Distinct().OrderBy(x => x.BoxName).Select(x => new VectorDescriptionItem("double", x.BoxName + "_SampleFrequency", DataTypeEnum.State)));
                list.AddRange(_boards.Distinct().OrderBy(x => x.BoxName).Select(x => new VectorDescriptionItem("double", x.BoxName + "_FilterSampleCount", DataTypeEnum.State)));
                list.Add(new VectorDescriptionItem("double", "SensorCtrl_LoopTime", DataTypeEnum.State));
            }

            CALog.LogInfoAndConsoleLn(LogID.A, $"{list.Count.ToString().PadLeft(2)} datapoints from {Title}");
            return list;
        }

        protected bool ShowQueue(List<string> args)
        {
            var sb = new StringBuilder($"NAME      {_loopTime.ToString("N0").PadLeft(4)}          AVERAGE       1         2         3         4         5         6         7         8         9         10       FREQUENCY");
            sb.Append(Environment.NewLine);
            foreach (var t in _values)
            {
                sb.Append($"{t.Value.Name.PadRight(22)}={t.Value.Value.ToString("N2").PadLeft(9)}  {t.Value.FilterToString()}   {t.Value.GetFrequency().ToString("N1")}");
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
                    var loopStart = DateTime.Now;
                    foreach (var board in _boards)
                    {
                        while (board.SafeHasDataInReadBuffer())
                        {
                            exBoard = board; // only used in exception
                            values.Clear();
                            numbers.Clear();
                            row = board.SafeReadLine();
                            if (Regex.IsMatch(row.Trim(), @"^\d+"))  // check that row starts with digit. 
                            {
                                values = row.Split(",".ToCharArray()).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                                numbers = values.Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToList();
                                ProcessLine(numbers, board);
                            }
                        }
                    }

                    Thread.Sleep(100);
                    _loopTime = DateTime.Now.Subtract(loopStart).TotalMilliseconds;
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

            foreach (var board in _boards)
            {
                if(board != null)
                    board.SafeClose();
            }

            CALog.LogInfoAndConsoleLn(LogID.A, $"Exiting {Title}.LoopForever() " + DateTime.Now.Subtract(start).TotalSeconds.ToString() + " seconds");
        }

        protected bool Stop(List<string> args)
        {
            _running = false;
            return true;
        }

        public void ProcessLine(IEnumerable<double> numbers, MCUBoard board)
        {
            int i = 0;
            var timestamp = DateTime.UtcNow;
            foreach (var value in numbers)
            {
                var sensor = _config.SingleOrDefault(x => x.BoxName == board.BoxName && x.PortNumber == i);
                if (sensor != null)
                {
                    _values[sensor].Value = value; // filter in here. 
                    _values[sensor].TimeStamp = timestamp;
                    _values[sensor].NumberOfPorts = GetNumberOfPorts(numbers); // we do not know this until here. 

                    HandleSaltLeakage(sensor);
                }

                i++;
            }
        }

        private string GetNumberOfPorts(IEnumerable<double> numbers)
        {
            switch (numbers.Count())
            {
                case 1: return "1x1";
                case 11: return "1x10";
                case 17: return "2x8";
                default: return "unknown";
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

        protected int GetHubID(IOconfInput sensor)
        {
            return _config.GroupBy(x => x.BoxName).Select(x => x.Key).ToList().IndexOf(sensor.BoxName);
        }

        public void Dispose()
        {
            _running = false;
            for (int i = 0; i < 100; i++)
            {
                foreach(var board in _boards)
                    if (board != null && board.IsOpen)
                            Thread.Sleep(10);
            }

            foreach (var board in _boards)
                if(board != null)
                    ((IDisposable)board).Dispose();
        }
    }
}
