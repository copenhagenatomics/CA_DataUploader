using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Text;
using CA_DataUploaderLib.IOconf;
using System.Text.RegularExpressions;
using Humanizer;
using System.Diagnostics;
using CA_DataUploaderLib.Extensions;

namespace CA_DataUploaderLib
{
    public class BaseSensorBox : IDisposable, ISubsystemWithVectorData
    {
        protected bool _running = true;
        public string Title { get; protected set; }

        protected CALogLevel _logLevel;
        protected CommandHandler _cmd;
        protected List<SensorSample> _values = new List<SensorSample>();
        protected List<MCUBoard> _boards = new List<MCUBoard>();
        protected int expectedHeaderLines = 8;
        private string commandHelp;
        private bool disposed;

        public BaseSensorBox(CommandHandler cmd, string commandName, string commandArgsHelp, string commandDescription, IEnumerable<IOconfInput> values) 
        { 
            Title = commandName;
            _cmd = cmd;
            _logLevel = IOconfFile.GetOutputLevel();
            _values = values.IsInitialized().Select(x => new SensorSample(x)).ToList();
            if (!_values.Any())
                return;  // no data

            if (cmd != null)
            {
                commandHelp = $"{commandName.ToLower() + " " + commandArgsHelp,-22}- {commandDescription}";
                cmd.AddCommand(commandName.ToLower(), ShowQueue);
                cmd.AddCommand("help", HelpMenu);
                cmd.AddCommand("escape", Stop);
            }

            _boards = _values.Where(x => !x.Input.Skip).Select(x => x.Input.Map.Board).Distinct().ToList();
            CALog.LogInfoAndConsoleLn(LogID.A, $"{commandName} boards: {_boards.Count()} values: {_values.Count()}");

            _running = true;
            new Thread(() => LoopForever()).Start();
        }

        public SensorSample GetValueByTitle(string title) =>
                _values.SingleOrDefault(x => x.Input.Name == title) ??
                throw new Exception(title + " not found in _config. Known names: " + string.Join(", ", _values.Select(x => x.Input.Name)));

        public IEnumerable<SensorSample> GetValues() => _values.Select(s => s.Clone());

        /// <remarks>Unlike <see cref="GetValues"/>, the instances returned by this method are updated as we get new data from the sensors.</remarks>
        public IEnumerable<SensorSample> GetAutoUpdatedValues() => _values;

        public virtual List<VectorDescriptionItem> GetVectorDescriptionItems()
        {
            var list = _values.Select(x => new VectorDescriptionItem("double", x.Input.Name, DataTypeEnum.Input)).ToList();
            CALog.LogInfoAndConsoleLn(LogID.A, $"{list.Count,2} datapoints from {Title}");
            return list;
        }

        protected bool ShowQueue(List<string> args)
        {
            var sb = new StringBuilder($"NAME      {GetAvgLoopTime(),4:N0}           ");
            sb.AppendLine();
            foreach (var t in _values)
            {
                sb.AppendLine($"{t.Input.Name,-22}={t.Value,9:N2}");
            }

            CALog.LogInfoAndConsoleLn(LogID.A, sb.ToString());
            return true;
        }

        private double GetAvgLoopTime() => _values.Average(x => x.ReadSensor_LoopTime);

        private static readonly Regex _startsWithDigitRegex = new Regex(@"^\s*(-|\d+)\s*");

        protected void LoopForever()
        {
            DateTime start = DateTime.Now;
            int badRow = 0;

            while (_running)
            {
                try
                {
                    ReadSensors();
                    CheckFails(); // check if any of the boards stopped responding. 
                    Thread.Sleep(100); //boards typically write a line every 100 ms
                }
                catch (Exception ex)
                {
                    CALog.LogInfoAndConsoleLn(LogID.A, ".", ex);
                    if (badRow++ > 10)
                    {
                        CALog.LogErrorAndConsoleLn(LogID.A, "Too many bad rows from thermocouple ports.. shutting down:");
                        _cmd?.Execute("escape");
                        _running = false;
                    }
                    else
                        Thread.Sleep(100); //boards typically write a line every 100 ms
                }
            }

            foreach (var board in _boards)
                board?.SafeClose();

            CALog.LogInfoAndConsoleLn(LogID.A, $"Exiting {Title}.LoopForever() " + DateTime.Now.Subtract(start).Humanize(5));
        }

        protected virtual void ReadSensors()
        {
            foreach (var board in _boards)
            {
                var hadDataAvailable = false;
                var timeInLoop = Stopwatch.StartNew();
                // We read all lines available. We make sure to exit within 100ms, to allow reads to other boards
                // and to avoid being stuck when data with errors is continuously returned by the board.
                // We use time instead of attempts as SafeHasDataInReadBuffer can continuously report there is data when a partial line is returned by a board that stalls,
                // which then consistently times out in SafeReadLine, making each loop iteration 2 seconds.
                while (board.SafeHasDataInReadBuffer() && timeInLoop.ElapsedMilliseconds < 100)
                {
                    hadDataAvailable = true;
                    var line = board.SafeReadLine(); // tries to read a full line for up to MCUBoard.ReadTimeout
                    try
                    {
                        var numbers = TryParseAsDoubleList(line);
                        if (numbers != null)
                            ProcessLine(numbers, board);
                        else // mostly responses to commands or headers on reconnects.
                            CALog.LogInfoAndConsoleLn(LogID.B, "Unhandled board response " + board.ToString() + " line: " + line);
                    }
                    catch (Exception ex)
                    {
                        CALog.LogErrorAndConsoleLn(LogID.B, "Failed handling board response " + board.ToString() + " line: " + line, ex);
                    }
                }

                if (!hadDataAvailable) 
                    // we expect data on every cycle (each 100 ms), as the boards normally write a line every 100 ms.
                    CALog.LogData(LogID.B, "No data available for " + board.ToString());
            }
        }

        /// <returns>the list of doubles, otherwise <c>null</c></returns>
        protected virtual List<double> TryParseAsDoubleList(string line)
        {
            if (!_startsWithDigitRegex.IsMatch(line))
                return null;

            return line.Split(",".ToCharArray())
                .Select(x => x.Trim())
                .Where(x => x.Length > 0)
                .Select(x => x.ToDouble())
                .ToList();
        }

        private void CheckFails()
        {
            List<string> failPorts = new List<string>();
            int maxDelay = 2000;
            bool reconnectLimitExceeded = false;
            foreach (var item in _values)
            {
                maxDelay = item.Input.Name.ToLower().Contains("luminox") ? 10000 : 2000;
                var msSinceLastRead = DateTime.UtcNow.Subtract(item.TimeStamp).TotalMilliseconds;
                if (msSinceLastRead > maxDelay && !item.Input.Skip)
                {
                    CALog.LogErrorAndConsoleLn(LogID.A, $"{Title} stale value detected for port: {item.Input.Name}{Environment.NewLine}{msSinceLastRead} milliseconds since last read - closing serial port to restablish connection");
                    if (item.Input.Map != null)
                        reconnectLimitExceeded |= !item.Input.Map.Board.SafeReopen(expectedHeaderLines);
                    failPorts.Add(item.Input.Name);
                }
            }

            if (reconnectLimitExceeded)
            {
                _cmd.Execute("escape");
                _running = false;
                CALog.LogErrorAndConsoleLn(LogID.A, $"Shutting down: {Title} unable to read from port: {string.Join(", ", failPorts)}{Environment.NewLine}Reconnection limit exceeded, latest valid read was more than {maxDelay} seconds old");
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
                    CALog.LogErrorAndConsoleLn(LogID.A, $"Salt leak detected from {sensor.Input.Name}={sensor.Value} {DateTime.Now:dd-MMM-yyyy HH:mm}");
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

        private bool HelpMenu(List<string> args)
        {
            CALog.LogInfoAndConsoleLn(LogID.A, commandHelp);
            return true;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposed)
                return;

            if (disposing)
            { // dispose managed state
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

            disposed = true;
        }
        
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method. See https://docs.microsoft.com/en-us/dotnet/standard/garbage-collection/implementing-dispose#dispose-and-disposebool
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
