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
        private string commandHelp;
        private bool disposed;
        private HashSet<MCUBoard> _lostBoards = new HashSet<MCUBoard>();

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
            // normally all boards have the same ms between reads, so for now just use the minimum value.
            var msBetweenReads = _boards.Min(b => b.ConfigSettings.MillisecondsBetweenReads); 

            while (_running)
            {
                Thread.Sleep(msBetweenReads);
                try
                {
                    ReadSensors();
                    CheckFails(); // check if any of the boards stopped responding. 
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
                        var numbers = TryParseAsDoubleList(board, line);
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
        protected virtual List<double> TryParseAsDoubleList(MCUBoard board, string line) => 
            board.ConfigSettings.Parser.TryParseAsDoubleList(line);

        private void CheckFails()
        {
            // late initialization for these 2, avoiding instance when there are no failures
            List<string> failedPorts = null;
            HashSet<MCUBoard> lostConnectionBoards = null; 
            foreach (var item in _values)
            {
                var msSinceLastRead = DateTime.UtcNow.Subtract(item.TimeStamp).TotalMilliseconds;
                if (_lostBoards.Contains(item.Input.Map?.Board) || (lostConnectionBoards?.Contains(item.Input.Map?.Board) ?? false)) 
                    continue; // ignore boards already reported as lost
                var settings = item.Input.Map?.Board?.ConfigSettings ?? BoardSettings.Default;
                if (msSinceLastRead <= settings.MaxMillisecondsWithoutNewValues || item.Input.Skip)
                    continue;
                CALog.LogErrorAndConsoleLn(LogID.A, $"{Title} stale value detected for port: {item.Input.Name}{Environment.NewLine}{msSinceLastRead} milliseconds since last read - closing serial port to restablish connection");
                if (item.Input.Map == null || item.Input.Map.Board.SafeReopen()) 
                    continue;
                LateInit(ref lostConnectionBoards).Add(item.Input.Map.Board);
                LateInit(ref failedPorts).Add(item.Input.Name);
            }

            if (lostConnectionBoards != null)
                HandleLostConnectionToBoards(lostConnectionBoards, failedPorts);
        }

        private void HandleLostConnectionToBoards(HashSet<MCUBoard> lostConnectionBoards, List<string> failedPorts)
        {
            var stopWhenLosingSensor = lostConnectionBoards.Any(b => b.ConfigSettings.StopWhenLosingSensor);
            if (stopWhenLosingSensor)
            {
                _cmd.Execute("escape");
                _running = false;
                CALog.LogErrorAndConsoleLn(LogID.A, $"Shutting down: {Title} unable to read from port: {string.Join(", ", failedPorts)}{Environment.NewLine}Reconnection limit exceeded");
                return;
            }

            var lostBoardsNames = string.Join(Environment.NewLine, lostConnectionBoards);
            CALog.LogErrorAndConsoleLn(LogID.A, $"{Title}: reconnect limit exceeded, these boards will be ignored: {Environment.NewLine}{lostBoardsNames}");
            _boards.RemoveAll(lostConnectionBoards.Contains);
            _lostBoards.UnionWith(lostConnectionBoards);
            foreach (var board in lostConnectionBoards)
            {
                try { board.Dispose(); }
                catch (Exception e) { CALog.LogException(LogID.A, e); }
            }
        }

        protected bool Stop(List<string> args)
        {
            _running = false;
            return true;
        }

        public virtual void ProcessLine(IEnumerable<double> numbers, MCUBoard board)
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

        private T LateInit<T>(ref T value) where T : new()
            => value = value ?? new T();

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
