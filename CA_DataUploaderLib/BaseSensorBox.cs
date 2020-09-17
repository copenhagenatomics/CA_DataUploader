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
        protected List<SensorSample> _values = new List<SensorSample>();
        protected List<MCUBoard> _boards = new List<MCUBoard>();

        public BaseSensorBox() { }

        public SensorSample GetValue(string sensorKey)
        {
            if(!_values.Any(x => (x.Input.BoxName + x.Input.PortNumber.ToString()) == sensorKey))
                throw new Exception(sensorKey + " not found in _values, count: " + _values.Count());
            return _values.Single(x => (x.Input.BoxName + x.Input.PortNumber.ToString()) == sensorKey);
        }

        public SensorSample GetValueByTitle(string title)
        {
            if (!_values.Any(x => x.Input.Name == title))
                throw new Exception(title + " not found in _config. Known names: " + string.Join(", ", _values.Select(x => x.Input.Name)));

            var temp = _values.SingleOrDefault(x => x.Input.Name == title);
            if (temp == null)
                throw new Exception(title + " not found in _values, count: " + _values.Count());

            return temp;
        }

        public IEnumerable<SensorSample> GetAllDatapoints()
        {
            return _values;  
        }

        public virtual List<VectorDescriptionItem> GetVectorDescriptionItems()
        {
            var list = _values.Select(x => new VectorDescriptionItem("double", x.Input.Name, DataTypeEnum.Input)).ToList();
            // list.AddRange(_config.Select(x => new VectorDescriptionItem("double", x.Name + "_latest", DataTypeEnum.Input)).ToList());
            if (_logLevel == CALogLevel.Debug)
            {
                foreach (var boxName in _values.Where(x => !x.Input.Skip).Select(x => x.Input.Map.BoxName).Distinct().OrderBy(x => x))
                {
                    list.Add(new VectorDescriptionItem("double", boxName + "_AvgSampleFrequency", DataTypeEnum.State));
                    list.Add(new VectorDescriptionItem("double", boxName + "_MinFilterSampleCount", DataTypeEnum.State));
                    list.Add(new VectorDescriptionItem("double", boxName + "_MaxLoopTime", DataTypeEnum.State));
                }
            }

            CALog.LogInfoAndConsoleLn(LogID.A, $"{list.Count.ToString().PadLeft(2)} datapoints from {Title}");
            return list;
        }

        protected bool ShowQueue(List<string> args)
        {
            if (_values.Count == 0)
                return false;

            var sb = new StringBuilder($"NAME      {GetAvgLoopTime().ToString("N0").PadLeft(4)}           ");
            sb.AppendLine();
            foreach (var t in _values)
            {
                sb.AppendLine($"{t.Input.Name.PadRight(22)}={t.Value.ToString("N2").PadLeft(9)}");
            }

            CALog.LogInfoAndConsoleLn(LogID.A, sb.ToString());
            return true;
        }

        private double GetAvgLoopTime()
        {
            return _values.Average(x => x.ReadSensor_LoopTime);
        }

        protected string _matchPattern = @"-?\d{1,10}(,\d{3})*(\.\d+)?";  // this will match any integer or decimal number. (but not scientific notation)

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
                    foreach (var board in _boards)
                    {
                        while (board.SafeHasDataInReadBuffer())
                        {
                            exBoard = board; // only used in exception
                            values.Clear();
                            numbers.Clear();
                            row = board.SafeReadLine();

                            if (Regex.IsMatch(row.Trim(), @"^(-|\d+)"))  // check that row starts with digit. 
                            {
                                values = row.Split(",".ToCharArray()).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                                numbers = values.Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToList();
                                ProcessLine(numbers, board);
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
                        badPorts.Add($"{exBoard.PortName}:{exBoard.serialNumber} = '{row}'");

                    badRow++;
                    if (badRow > 10)
                    {
                        CALog.LogErrorAndConsoleLn(LogID.A, "Too many bad rows from thermocouple ports.. shutting down:");
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

        private int _failCount = 0;

        private void CheckFails()
        {
            List<string> failPorts = new List<string>();
            int maxDelay = 2000;
            foreach (var item in _values)
            {
                maxDelay = (item.Input.Name.ToLower().Contains("luminox")) ? 10000 : 2000;
                if (DateTime.UtcNow.Subtract(item.TimeStamp).TotalMilliseconds > maxDelay)
                {
                    item.ReadSensor_LoopTime = 0;
                    item.Input.Map.Board.SafeClose();
                    _failCount++;
                    failPorts.Add(item.Input.Name);
                }
            }

            if (_failCount > 200)
            {
                _cmd.Execute("escape");
                _running = false;
                CALog.LogErrorAndConsoleLn(LogID.A, $"Shutting down: {Title} unable to read from port: {string.Join(", ", failPorts)}{Environment.NewLine}Failed {_failCount} times read operations in a row where latest valid read was more than {maxDelay} seconds old");
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
                var sensor = _values.SingleOrDefault(x => x.Input.BoxName == board.BoxName && x.Input.PortNumber == i);
                if (sensor != null)
                {
                    sensor.Value = value;
                    sensor.ReadSensor_LoopTime = timestamp.Subtract(sensor.TimeStamp).TotalMilliseconds;
                    sensor.TimeStamp = timestamp;

                    HandleSaltLeakage(sensor);
                }

                i++;
            }
        }

        private void HandleSaltLeakage(SensorSample sensor)
        {
            if (sensor.GetType() == typeof(IOconfSaltLeakage))
            {
                if (sensor.Value < 3000 && sensor.Value > 0)  // Salt leakage algorithm. 
                {
                    CALog.LogErrorAndConsoleLn(LogID.A, $"Salt leak detected from {sensor.Input.Name}={sensor.Value} {DateTime.Now.ToString("dd-MMM-yyyy HH:mm")}");
                    sensor.Value = 1d;
                    if (_cmd != null)
                        _cmd.Execute("escape"); // make the whole system shut down. 
                }
                else
                {
                    sensor.Value = 0d; // no leakage
                }
            }
        }

        protected int GetHubID(SensorSample sensor)
        {
            return _values.GroupBy(x => x.Input.BoxName).Select(x => x.Key).ToList().IndexOf(sensor.Input.BoxName);
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
