using CA_DataUploaderLib.IOconf;
using Humanizer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace CA_DataUploaderLib
{
    public class HeatingController : IDisposable
    {
        public List<SensorSample> ValidHatSensors { get; private set; }
        public double Voltage = 230;
        public int HeaterOnTimeout = 60;
        private bool _running = true;
        private CALogLevel _logLevel = CALogLevel.Normal;
        private double _offTemperature = 0;
        private double _lastTemperature = 0;
        private DateTime _startTime;
        private readonly List<HeaterElement> _heaters = new List<HeaterElement>();
        private readonly List<string> _ovenHistory = new List<string>();
        protected CommandHandler _cmd;

        public HeatingController(BaseSensorBox caThermalBox, CommandHandler cmd)
        {
            _cmd = cmd;

            // map all heaters, sensors and ovens. 
            var heaters = IOconfFile.GetHeater().ToList();
            var oven = IOconfFile.GetOven().ToList();
            var sensors = caThermalBox.GetAutoUpdatedValues().ToList();
            foreach (var heater in heaters)
            {
                var ovenSensor = oven.SingleOrDefault(x => x.HeatingElement.Name == heater.Name)?.TypeK.Name;
                int area = oven.SingleOrDefault(x => x.HeatingElement.Name == heater.Name && x.OvenArea > 0)?.OvenArea??-1;
                _heaters.Add(new HeaterElement(area, heater, sensors.Where(x => x.Input.Name == ovenSensor)));
            }

            if (!_heaters.Any())
                return;

            var unreachableBoards = heaters.Where(h => h.Map.Board == null).GroupBy(h => h.Map).ToList();
            foreach (var board in unreachableBoards)
                CALog.LogErrorAndConsoleLn(LogID.A, $"Missing board {board.Key} for heaters {string.Join(",",board.Select(h=> h.Name))}");
            if (unreachableBoards.Count > 0)
                throw new NotSupportedException("Running with missing heaters is not currently supported");

            new Thread(() => this.LoopForever()).Start();
            cmd.AddCommand("escape", Stop);
            for (int i = 0; i < 20; i++)
            {
                Thread.Sleep(200); // waiting for temperature date to arrive, this ensure only valid heating elements are created. 
                if (_heaters.Any())
                {
                    cmd.AddCommand("help", HelpMenu);
                    cmd.AddCommand("emergencyshutdown", EmergencyShutdown);
                    cmd.AddCommand("heater", Heater);
                    if (oven.Any())
                    {
                        cmd.AddCommand("oven", Oven);
                        if (IOconfFile.GetOutputLevel() == CALogLevel.Debug)
                            cmd.AddCommand("ovenhistory", OvenHistory);
                    }

                    break; // exit the for loop
                }
            }
        }

        private bool EmergencyShutdown(List<string> arg)
        {
            AllOff();
            return true;
        }

        private bool HelpMenu(List<string> args)
        {
            CALog.LogInfoAndConsoleLn(LogID.A, $"heater [name] on/off      - turn the heater with the given name in IO.conf on and off");
            if (_heaters.Any(x => !x.IsArea(-1)))
            {
                CALog.LogInfoAndConsoleLn(LogID.A, $"oven [0 - 800] [0 - 800]  - where the integer value is the oven temperature top and bottom region");
                if (_logLevel == CALogLevel.Debug) CALog.LogInfoAndConsoleLn(LogID.A, $"ovenhistory [x]           - Show a list of the last x oven commands");
            }

            return true;
        }

        private bool Stop(List<string> args)
        {
            _running = false;
            return true;
        }

        // usage: oven 200 220 400
        private bool Oven(List<string> args)
        {
            _cmd.AssertArgs(args, 2);
            if (args[1] == "off")
            {
                _heaters.ForEach(x => x.SetTemperature(0));
            }
            else
            {
                var areas = IOconfFile.GetOven().GroupBy(x => x.OvenArea).OrderBy(x => x.Key);
                int i = 1;
                int areaTemp = 300; // default value;
                foreach (var area in areas)
                {
                    areaTemp = CommandHandler.GetCmdParam(args, i++, areaTemp);
                    _heaters.Where(x => x.IsArea(area.Key)).ToList().ForEach(x => x.SetTemperature(areaTemp));
                }
            }

            _ovenHistory.Add(DateTime.Now.ToString("MMM dd HH:mm:ss ") + string.Join(" ", args));
            if (_heaters.Any(x => x.IsActive))
                _cmd.Execute("light main on");
            else
                _cmd.Execute("light main off");
            return true;
        }

        private bool OvenHistory(List<string> args)
        {
            if (_ovenHistory.Count == 0)
                return false;

            int nlines = Math.Min(_ovenHistory.Count, CommandHandler.GetCmdParam(args, 1, 1000000000));
            int nSkip = _ovenHistory.Count - nlines;
            _ovenHistory.Skip(nSkip).ToList().ForEach(x => CALog.LogInfoAndConsoleLn(LogID.A, x));
            return true;
        }

        public bool Heater(List<string> args)
        {
            var heater = _heaters.SingleOrDefault(x => x.Name() == args[1].ToLower());
            if (heater == null)
                return false;

            if (args[2].ToLower() == "on")
            {
                heater.IsOn = true;
                heater.ManualMode = true;
                HeaterOn(heater);
            }
            else
            {
                heater.IsOn = false;
                heater.ManualMode = false;
                HeaterOff(heater);
            }

            return true;
        }

        private void LoopForever()
        {
            _startTime = DateTime.UtcNow;
            _logLevel = IOconfFile.GetOutputLevel();
            var loopStart = DateTime.UtcNow;
            while (_running)
            {
                try
                {
                    foreach (var heater in _heaters)
                    {
                        if (heater.IsOn && heater.MustTurnOff())
                        {
                            _offTemperature = heater.MaxSensorTemperature();
                            _lastTemperature = heater.lastTemperature;
                            heater.IsOn = false;
                            HeaterOff(heater);
                        }
                        else if (!heater.IsOn && heater.CanTurnOn())
                        {
                            heater.IsOn = true;
                            HeaterOn(heater);
                        }
                    }

                    foreach (var box in _heaters.Select(x => x.Board()).Distinct())
                    {
                        var values = SwitchBoardBase.ReadInputFromSwitchBoxes(box);
                        GetCurrentValues(box, values);
                    }

                    // check if any of the boards stopped responding. 
                    bool reconnectLimitExceeded = false;
                    foreach (var heater in _heaters)
                    {
                        if (heater._ioconf.BoxName == "ArduinoHack")
                            continue;

                        if (DateTime.UtcNow.Subtract(heater.Current.TimeStamp).TotalMilliseconds > 2000)
                        {
                            reconnectLimitExceeded |= !heater._ioconf.Map.Board.SafeReopen();
                        }
                    }

                    if (reconnectLimitExceeded)
                    {
                        _cmd.Execute("escape");
                        _running = false;
                        CALog.LogErrorAndConsoleLn(LogID.A, "Shutting down: HeatingController unable to read from port");
                    }

                    Thread.Sleep(150); // if we read too often, then we will not get a full line, thus no match. 
                }
                catch (ArgumentException ex)
                {
                    CALog.LogErrorAndConsoleLn(LogID.A, ex.ToString());
                    _running = false;
                }
                catch (TimeoutException ex)
                {
                    CALog.LogErrorAndConsoleLn(LogID.A, ex.ToString());
                    AllOff();
                    _heaters.Clear(); 
                }
                catch (Exception ex)
                {
                    CALog.LogErrorAndConsoleLn(LogID.A, ex.ToString());
                }
            }

            CALog.LogInfoAndConsoleLn(LogID.A, "Exiting HeatingController.LoopForever() " + DateTime.UtcNow.Subtract(_startTime).Humanize(5));
            AllOff();
        }

        private void GetCurrentValues(MCUBoard board, List<double> values)
        {
            if (values.Count() == 4)
            {
                foreach (var heater in _heaters.Where(x => x.Board() == board))
                {
                    heater.Current.Value = values[heater._ioconf.PortNumber - 1];

                    // this is for extra safety to make sure heaters are on/off when expected to be. 
                    if (heater.MustResendOnCommand())
                    {
                        HeaterOn(heater);
                        CALog.LogData(LogID.A, $"on.={heater.Name()}-{heater.MaxSensorTemperature():N0}, v#={string.Join(", ", values)}, WB={board.BytesToWrite}{Environment.NewLine}");
                    }

                    if (heater.MustResendOffCommand())
                    {
                        HeaterOff(heater);
                        CALog.LogData(LogID.A, $"off.={heater.Name()}-{heater.MaxSensorTemperature():N0}, v#={string.Join(", ", values)}, WB={board.BytesToWrite}{Environment.NewLine}");
                    }
                }
            }
        }

        private void AllOff()
        {
            _heaters.ForEach(x => x.SetTemperature(0));
            foreach (var box in _heaters.Select(x => x.Board()).Where(x => x != null).Distinct())
                box.SafeWriteLine("off");

            CALog.LogInfoAndConsoleLn(LogID.A, "All heaters are off");
        }

        protected virtual void HeaterOff(HeaterElement heater)
        {
            try
            {
                heater.LastOff = DateTime.UtcNow;
                heater.Board().SafeWriteLine($"p{heater._ioconf.PortNumber} off");
                if (_logLevel == CALogLevel.Debug)
                    CALog.LogData(LogID.B, $"wrote p{heater._ioconf.PortNumber} off to {heater.Board().BoxName}");
            }
            catch (TimeoutException)
            {
                throw new TimeoutException($"Unable to write to {heater.Board().BoxName}");
            }
        }

        protected virtual void HeaterOn(HeaterElement heater)
        {
            try
            {
                heater.LastOn = DateTime.UtcNow;
                heater.Board().SafeWriteLine($"p{heater._ioconf.PortNumber} on {HeaterOnTimeout}");
                if(_logLevel == CALogLevel.Debug)
                    CALog.LogData(LogID.B, $"wrote p{heater._ioconf.PortNumber} on {HeaterOnTimeout} to {heater.Board().BoxName}");
            }
            catch (TimeoutException)
            {
                throw new TimeoutException($"Unable to write to {heater.Board().BoxName}");
            }
        }

        public List<SensorSample> GetStates()
        {
            var list = new List<SensorSample>();
            if (_logLevel == CALogLevel.Debug)
            {
                list.Add(new SensorSample("off_temperature", _offTemperature));
                list.Add(new SensorSample("last_temperature", _lastTemperature));
            }

            return list;
        }

        public IEnumerable<SensorSample> GetPower()
        {
            var powerValues = _heaters.Select(x => x.Current.Clone());
            var states =_heaters.Select(x => new SensorSample(x.Name() + "_On/Off", x.IsOn ? 1.0 : 0.0));
            var values = powerValues.Concat(states);
            if (_logLevel == CALogLevel.Debug)
            {
                var loopTimes = _heaters.Select(x => new SensorSample(x.Name() + "_LoopTime", x.Current.ReadSensor_LoopTime));
                return values.Concat(loopTimes);
            }

            return values;
        }

        /// <summary>
        /// Gets all the values in the order specified by <see cref="GetVectorDescriptionItems"/>.
        /// </summary>
        public IEnumerable<SensorSample> GetValues()
        {
            return GetPower().Concat(GetStates());
        }

        public List<VectorDescriptionItem> GetVectorDescriptionItems()
        {
            var list = _heaters.Select(x => new VectorDescriptionItem("double", x.Current.Name, DataTypeEnum.Input)).ToList();
            list.AddRange(_heaters.Select(x => new VectorDescriptionItem("double", x.Name() + "_On/Off", DataTypeEnum.Output)));
            if (_logLevel == CALogLevel.Debug)
            {
                list.AddRange(_heaters.Select(x => new VectorDescriptionItem("double", x.Name() + "_LoopTime", DataTypeEnum.State)));
                list.Add(new VectorDescriptionItem("double", "off_temperature", DataTypeEnum.State));
                list.Add(new VectorDescriptionItem("double", "last_temperature", DataTypeEnum.State));
            }

            CALog.LogInfoAndConsoleLn(LogID.A, $"{list.Count,2} datapoints from HeatingController");
            return list;
        }

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            _running = false;
            if (!disposedValue)
            {
                disposedValue = true;
            }
        }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
        }

        #endregion



    }
}
