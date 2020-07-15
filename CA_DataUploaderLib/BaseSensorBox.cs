using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Globalization;
using System.Text;
using CA_DataUploaderLib.IOconf;
using System.Text.RegularExpressions;
using Humanizer;

namespace CA_DataUploaderLib
{
    public class BaseSensorBox : IDisposable
    {
        protected bool _running = true;
        public string Title { get; protected set; }

        protected CALogLevel _logLevel;
        protected CommandHandler _cmd;
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

            var list = new List<double>();
            foreach(var board in _values.Where(x => !x.Key.Skip).Select(x => x.Value).GroupBy(x => x.Input.BoxName).OrderBy(x => x.Key))
            {
                list.Add(board.Average(x => x.GetFrequency()));
                list.Add(board.Min(x => x.FilterCount()));
                list.Add(board.Max(x => x.ReadSensor_LoopTime));
            }

            return list;
        }

        public virtual List<VectorDescriptionItem> GetVectorDescriptionItems()
        {
            var list = _config.Select(x => new VectorDescriptionItem("double", x.Name, DataTypeEnum.Input)).ToList();
            // list.AddRange(_config.Select(x => new VectorDescriptionItem("double", x.Name + "_latest", DataTypeEnum.Input)).ToList());
            if (_logLevel == CALogLevel.Debug)
            {
                foreach (var boxName in _config.Where(x => !x.Skip).Select(x => x.Map.BoxName).Distinct().OrderBy(x => x))
                {
                    list.Add(new VectorDescriptionItem("double", boxName + "_AvgSampleFrequency", DataTypeEnum.State));
                    list.Add(new VectorDescriptionItem("double", boxName + "_MinFilterSampleCount", DataTypeEnum.State));
                    list.Add(new VectorDescriptionItem("double", boxName + "_MaxLoopTime", DataTypeEnum.State));
                }
            }

            CALog.LogInfoAndConsoleLn(LogID.A, $"{list.Count.ToString().PadLeft(2)} datapoints from {Title}");
            return list;
        }

        private TimeSpan _filterZero = new TimeSpan(0, 0, 0);

        protected bool ShowQueue(List<string> args)
        {
            if(_values.Count == 0)
                return false;

            var sb = new StringBuilder($"NAME      {GetAvgLoopTime().ToString("N0").PadLeft(4)}           ");
            if (_values.First().Value.FilterLength == _filterZero)
            {
                sb.AppendLine();
                foreach (var t in _values)
                {
                    sb.AppendLine($"{t.Value.Name.PadRight(22)}={t.Value.Value.ToString("N2").PadLeft(9)}");
                }
            }
            else
            {
                sb.Append("AVERAGE       1");
                for (int i = 2; i <= _values.First().Value.FilterCount(); i++)
                    sb.Append(i.ToString().PadLeft(10));

                sb.Append("     FREQUENCY");
                sb.AppendLine();
                foreach (var t in _values)
                {
                    sb.Append($"{t.Value.Name.PadRight(22)}={t.Value.Value.ToString("N2").PadLeft(9)}  {t.Value.FilterToString()}   {t.Value.GetFrequency().ToString("N1")}");
                    sb.AppendLine();
                }
            }            

            CALog.LogInfoAndConsoleLn(LogID.A, sb.ToString());
            return true;
        }

        private double GetAvgLoopTime()
        {
            return _values.Values.Average(x => x.ReadSensor_LoopTime);
        }

        protected void LoopForever()
        {
            List<double> numbers = new List<double>();
            List<string> values = new List<string>();
            List<MCUBoard> skipBoard = new List<MCUBoard>();
            DateTime start = DateTime.Now;
            string row = string.Empty;
            int badRow = 0;
            List<string> badPorts = new List<string>();
            MCUBoard exBoard = null;

            while (_running)
            {
                try
                {
                    foreach (var board in _boards)
                    {
                        if (!skipBoard.Contains(board))
                        {
                            while (board.SafeHasDataInReadBuffer())
                            {
                                exBoard = board; // only used in exception
                                values.Clear();
                                numbers.Clear();
                                row = board.SafeReadLine();
                                if (Regex.IsMatch(row.Trim(), "\\[SLPM\\], Liter:")) // hack to skip FlowAndPressure boards. 
                                    skipBoard.Add(board);

                                if (Regex.IsMatch(row.Trim(), @"^\d+"))  // check that row starts with digit. 
                                {
                                    values = row.Split(",".ToCharArray()).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                                    numbers = values.Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToList();
                                    ProcessLine(numbers, board);
                                }
                            }
                        }
                    }

                    // check if any of the boards stopped responding. 
                    CheckFails();

                    Thread.Sleep(100);
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
                        if(_cmd != null)
                            _cmd.Execute("escape");

                        _running = false;
                    }
                }
            }

            foreach (var board in _boards)
            {
                if(board != null)
                    board.SafeClose();
            }

            CALog.LogInfoAndConsoleLn(LogID.A, $"Exiting {Title}.LoopForever() " + DateTime.Now.Subtract(start).Humanize(5));
        }

        private int failCount = 0;

        private void CheckFails()
        {
            List<string> failPorts = new List<string>();
            foreach (var item in _values.Values)
            {
                var maxDelay = (item.Input.Name.ToLower().Contains("luminox")) ? 10000 : 2000;
                if (DateTime.UtcNow.Subtract(item.TimeStamp).TotalMilliseconds > maxDelay)
                {
                    item.ReadSensor_LoopTime = 0;
                    item.Input.Map.Board.SafeClose();
                    failCount++;
                    failPorts.Add(item.Input.Name);
                }
            }

            if (failCount > 200)
            {
                _cmd.Execute("escape");
                _running = false;
                CALog.LogErrorAndConsoleLn(LogID.A, $"Shutting down: {Title} unable to read from port: {string.Join(", ", failPorts)}");
            }
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
                    CALog.LogErrorAndConsoleLn(LogID.A, $"Salt leak detected from {sensor.Name}={_values[sensor].Value} {DateTime.Now.ToString("dd-MMM-yyyy HH:mm")}");
                    _values[sensor].Value = 1d;
                    if (_cmd != null)
                        _cmd.Execute("escape"); // make the whole system shut down. 
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
