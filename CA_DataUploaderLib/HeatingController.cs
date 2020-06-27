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
        private double _loopTime = 0;
        private double _offTemperature = 0;
        private double _lastTemperature = 0;
        private DateTime _startTime;
        private List<HeaterElement> _heaters = new List<HeaterElement>();
        private List<string> _ovenHistory = new List<string>();
        protected CommandHandler _cmdHandler;

        public HeatingController(BaseSensorBox caThermalBox, CommandHandler cmd)
        {
            _cmdHandler = cmd;

            // map all heaters, sensors and ovens. 
            var heaters = IOconfFile.GetOven().GroupBy(x => x.HeatingElement);
            var sensors = caThermalBox.GetAllDatapoints().ToList();
            foreach (var heater in heaters.Where(x => x.Any(y => y.OvenArea > 0)))
            {
                int area = heater.First(x => x.OvenArea > 0).OvenArea;
                var maxSensors = heater.Where(x => x.IsMaxTemperatureSensor).Select(x => x.TypeK.Name).ToList();
                var ovenSensor = heater.Where(x => !x.IsMaxTemperatureSensor).Select(x => x.TypeK.Name).ToList();
                _heaters.Add(new HeaterElement(area, heater.Key, sensors.Where(x => maxSensors.Contains(x.Name)), sensors.Where(x => ovenSensor.Contains(x.Name))));
            }

            if (!_heaters.Any())
                return;

            new Thread(() => this.LoopForever()).Start();
            cmd.AddCommand("escape", Stop);
            for (int i = 0; i < 20; i++)
            {
                Thread.Sleep(200); // waiting for temperature date to arrive, this ensure only valid heating elements are created. 
                if (_heaters.Any())
                {
                    cmd.AddCommand("help", HelpMenu);
                    cmd.AddCommand("oven", Oven);
                    if(IOconfFile.GetOutputLevel() == CALogLevel.Debug) cmd.AddCommand("ovenhistory", OvenHistory);
                    cmd.AddCommand("heater", Heater);
                    break; // exit the for loop
                }
            }
        }

        private bool HelpMenu(List<string> args)
        {
            CALog.LogInfoAndConsoleLn(LogID.A, $"heater [name] on/off      - turn the heater with the given name in IO.conf on and off");
            CALog.LogInfoAndConsoleLn(LogID.A, $"oven [0 - 800] [0 - 800]  - where the integer value is the oven temperature top and bottom region");
            if (IOconfFile.GetOutputLevel() == CALogLevel.Debug) CALog.LogInfoAndConsoleLn(LogID.A, $"ovenhistory [x]           - Show a list of the last x oven commands");
            return true;
        }

        private bool Stop(List<string> args)
        {
            _running = false;
            return true;
        }

        private bool Oven(List<string> args)
        {
            _cmdHandler.AssertArgs(args, 2);
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

            _ovenHistory.Add(DateTime.Now.ToString("MMM dd HH:mm:ss") + string.Join(" ", args));
            if (_heaters.Any(x => x.IsActive))
                _cmdHandler.Execute("light on");
            else
                _cmdHandler.Execute("light off");
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
            var heater = _heaters.SingleOrDefault(x => x.name() == args[1].ToLower());
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
            var logLevel = IOconfFile.GetOutputLevel();
            while (_running)
            {
                try
                {
                    var loopStart = DateTime.UtcNow;
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

                    Thread.Sleep(120); // if we read too often, then we will not get a full line, thus no match. 
                    _loopTime = DateTime.UtcNow.Subtract(loopStart).TotalMilliseconds;
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
            if (values.Count() > 0)
            {
                foreach (var heater in _heaters.Where(x => x.Board() == board))
                {
                    heater.Current.Value = values[heater._ioconf.PortNumber - 1];

                    // this is a hot fix to make sure heaters are on/off. 
                    if (heater.Current.TimeoutValue == 0 && heater.IsOn && heater.LastOn.AddSeconds(2) < DateTime.UtcNow)
                    {
                        HeaterOn(heater);
                        CALog.LogData(LogID.A, $"on.={heater.MaxSensorTemperature().ToString("N0")}, v#={string.Join(", ", values)}, WB={board.BytesToWrite}{Environment.NewLine}");
                    }

                    if (heater.Current.TimeoutValue > 0 && !heater.IsOn && heater.LastOff.AddSeconds(2) < DateTime.UtcNow)
                    {
                        HeaterOff(heater);
                        CALog.LogData(LogID.A, $"off.={heater.MaxSensorTemperature().ToString("N0")}, v#={string.Join(", ", values)}, WB={board.BytesToWrite}{Environment.NewLine}");
                    }
                }
            }
        }

        private void AllOff()
        {
            foreach (var box in _heaters.Select(x => x.Board()).Distinct())
                box.SafeWriteLine("off");

            CALog.LogInfoAndConsoleLn(LogID.A, "All heaters are off");
        }

        protected virtual void HeaterOff(HeaterElement heater)
        {
            try
            {
                heater.LastOff = DateTime.UtcNow;
                heater.Board().SafeWriteLine($"p{heater._ioconf.PortNumber} off");
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
            }
            catch (TimeoutException)
            {
                throw new TimeoutException($"Unable to write to {heater.Board().BoxName}");
            }
        }

        public List<double> GetStates()
        {
            var list = _heaters.Select(x => x.IsOn ? 1.0 : 0.0).ToList();
            if (IOconfFile.GetOutputLevel() == CALogLevel.Debug)
            {
                list.Add(_offTemperature);
                list.Add(_lastTemperature);
                list.Add(_loopTime);
                list.Add(_heaters.Max(x => x.Current.GetFrequency()));
            }

            return list;
        }

        public List<double> GetPower()
        {
            return _heaters.Select(x => x.Current.TimeoutValue).ToList();
        }

        public List<VectorDescriptionItem> GetVectorDescriptionItems()
        {
            var list = _heaters.Select(x => new VectorDescriptionItem("double", x.name() + "_Power", DataTypeEnum.Input)).ToList();
            list.AddRange(_heaters.Select(x => new VectorDescriptionItem("double", x.name() + "_On/Off", DataTypeEnum.Output)));
            if (IOconfFile.GetOutputLevel() == CALogLevel.Debug)
            {
                list.Add(new VectorDescriptionItem("double", "off_temperature", DataTypeEnum.State));
                list.Add(new VectorDescriptionItem("double", "last_temperature", DataTypeEnum.State));
                list.Add(new VectorDescriptionItem("double", "HeatingCtrl_LoopTime", DataTypeEnum.State));
                list.Add(new VectorDescriptionItem("double", "CurrentSamplingFrequency", DataTypeEnum.State));
            }

            CALog.LogInfoAndConsoleLn(LogID.A, $"{list.Count.ToString().PadLeft(2)} datapoints from HeatingController");
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
