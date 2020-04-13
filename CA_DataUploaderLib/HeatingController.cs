﻿using CA_DataUploaderLib.IOconf;
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
        private List<HeaterElement> _heaters = new List<HeaterElement>();
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
                    cmd.AddCommand("heater", Heater);
                    break; // exit the for loop
                }
            }
        }

        private bool HelpMenu(List<string> args)
        {
            CALog.LogInfoAndConsoleLn(LogID.A, $"heater [name] on/off      - turn the heater with the given name in IO.conf on and off");
            CALog.LogInfoAndConsoleLn(LogID.A, $"oven [0 - 800] [0 - 800]  - where the integer value is the oven temperature top and bottom region");
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
            var areas = IOconfFile.GetOven().GroupBy(x => x.OvenArea);
            int i = 1;
            int areaTemp = 300; // default value;
            foreach (var area in areas)
            {
                areaTemp = CommandHandler.GetCmdParam(args, i++, areaTemp);
                _heaters.Where(x => x.IsArea(area.Key)).ToList().ForEach(x => x.SetTemperature(areaTemp));
            }

            if (_heaters.Any(x => x.IsActive))
                _cmdHandler.Execute("light on");
            else
                _cmdHandler.Execute("light off");
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
            DateTime start = DateTime.Now;
            var logLevel = IOconfFile.GetOutputLevel();
            while (_running)
            {
                try
                {
                    var loopStart = DateTime.Now;
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

                    Thread.Sleep(300); // if we read too often, then we will not get a full line, thus no match. 
                    _loopTime = DateTime.Now.Subtract(loopStart).TotalMilliseconds;
                }
                catch (ArgumentException ex)
                {
                    CALog.LogErrorAndConsole(LogID.A, ex.ToString());
                    _running = false;
                }
                catch (TimeoutException ex)
                {
                    CALog.LogErrorAndConsole(LogID.A, ex.ToString());
                    AllOff();
                    _heaters.Clear(); 
                }
                catch (Exception ex)
                {
                    CALog.LogErrorAndConsole(LogID.A, ex.ToString());
                }
            }

            CALog.LogInfoAndConsoleLn(LogID.A, "Exiting HeatingController.LoopForever() " + DateTime.Now.Subtract(start).TotalSeconds.ToString() + " seconds");
            AllOff();
        }

        private void GetCurrentValues(MCUBoard board, List<double> values)
        {
            if (values.Count() > 0)
            {
                foreach (var heater in _heaters.Where(x => x.Board() == board))
                {
                    heater.Current = values[heater._ioconf.PortNumber - 1];

                    // this is a hot fix to make sure heaters are on/off. 
                    if (heater.Current == 0 && heater.IsOn && heater.LastOn.AddSeconds(2) < DateTime.UtcNow)
                    {
                        HeaterOn(heater);
                        CALog.LogInfoAndConsole(LogID.A, $"on={heater.MaxSensorTemperature().ToString("N0")}, ");
                    }

                    if (heater.Current > 0 && !heater.IsOn && heater.LastOff.AddSeconds(2) < DateTime.UtcNow)
                    {
                        HeaterOff(heater);
                        CALog.LogInfoAndConsole(LogID.A, $"off={heater.MaxSensorTemperature().ToString("N0")}, ");
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
            }

            return list;
        }

        public List<double> GetPower()
        {
            return _heaters.Select(x => x.Current).ToList();
        }

        public List<VectorDescriptionItem> GetVectorDescriptionItems()
        {
            var list = _heaters.Select(x => new VectorDescriptionItem("double", x.name() + "_Power", DataTypeEnum.Input)).ToList();
            list.AddRange(_heaters.Select(x => new VectorDescriptionItem("double", x.name() + "_On/Off", DataTypeEnum.Output)));
            if (IOconfFile.GetOutputLevel() == CALogLevel.Debug)
            {
                list.Add(new VectorDescriptionItem("double", "off_temperature", DataTypeEnum.Input));
                list.Add(new VectorDescriptionItem("double", "last_temperature", DataTypeEnum.Input));
                list.Add(new VectorDescriptionItem("double", "HeatingCtrl_LoopTime", DataTypeEnum.Input));
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
