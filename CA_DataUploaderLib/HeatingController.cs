using CA_DataUploaderLib.IOconf;
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
        private List<HeaterElement> _heaters = new List<HeaterElement>();
        private BaseSensorBox _caThermalBox;
        protected CommandHandler _cmdHandler;
        private int _sendCount = 0;

        public HeatingController(BaseSensorBox caThermalBox, CommandHandler cmd)
        {
            _caThermalBox = caThermalBox;
            _cmdHandler = cmd;

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
            var areas = IOconfFile.GetOven();
            int i = 1;
            int areaTemp = 300; // default value;
            foreach (var area in areas)
            {
                areaTemp = CommandHandler.GetCmdParam(args, i++, areaTemp);
                var heatingElementNames = area.Select(x => x.HeatingElement.Name).ToList();
                foreach (var heater in _heaters.Where(x => heatingElementNames.Contains(x.ioconf.Name)))
                    heater.SetTemperature(areaTemp);
            }

            if (_heaters.Any(x => x.TargetTemperature > 0))
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
                heater.LastOn = DateTime.UtcNow;
                heater.ManualMode = true;
                HeaterOn(heater);
            }
            else
            {
                heater.IsOn = false;
                heater.LastOff = DateTime.UtcNow;
                heater.ManualMode = false;
                HeaterOff(heater);
            }

            return true;
        }

        private void LoopForever()
        {
            int i = 0;
            DateTime start = DateTime.Now;
            var logLevel = IOconfFile.GetOutputLevel();
            while (_running)
            {
                try
                {
                    foreach (var heater in _heaters)
                    {
                        if (heater.IsOn && heater.MustTurnOff())
                        {
                            heater.LastOff = DateTime.UtcNow;
                            heater.IsOn = false;
                            HeaterOff(heater);
                        }
                        else if (!heater.IsOn && heater.CanTurnOn())
                        {
                            heater.LastOn = DateTime.UtcNow;
                            heater.IsOn = true;
                            HeaterOn(heater);
                        }
                    }

                    foreach (var box in _heaters.Select(x => x.Board()).Distinct())
                    {
                        var values = SwitchBoardBase.ReadInputFromSwitchBoxes(box);
                        GetCurrentValues(box, values);
                    }

                    Thread.Sleep(300);
                    if (i++ % 20 == 0)   // check for new termocouplers every 10 seconds. 
                        CheckForNewThermocouplers();
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
                    heater.Current = values[heater.ioconf.PortNumber - 1];

                    // this is a hot fix to make sure heaters are on/off. 
                    if (_sendCount++ == 10)
                    {
                        _sendCount = 0;
                        if (heater.Current == 0 && heater.IsOn)
                        {
                            CALog.LogData(LogID.B, "_");
                            HeaterOn(heater);
                        }

                        if (heater.Current > 0 && !heater.IsOn)
                        {
                            CALog.LogData(LogID.B, "_");
                            HeaterOff(heater);
                        }
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
                heater.Board().SafeWriteLine($"p{heater.ioconf.PortNumber} off");
                CALog.LogData(LogID.B, heater.ToString());
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
                heater.Board().SafeWriteLine($"p{heater.ioconf.PortNumber} on {HeaterOnTimeout}");
                CALog.LogData(LogID.B, heater.ToString());
            }
            catch (TimeoutException)
            {
                throw new TimeoutException($"Unable to write to {heater.Board().BoxName}");
            }
        }

        private void CheckForNewThermocouplers()
        {
            var sensors = _caThermalBox.GetAllValidDatapoints();
            
            // add new heaters
            foreach(var oven in IOconfFile.GetOven().SelectMany(x => x))
            {
                var sensor = sensors.SingleOrDefault(x => x.Name == oven.TypeK.Name);
                if (sensor != null)
                {
                    var heater = _heaters.SingleOrDefault(x => x.ioconf == oven.HeatingElement);
                    if (heater == null)
                        _heaters.Add(new HeaterElement(oven.HeatingElement, sensor));
                    else if(!heater.sensors.Contains(sensor))
                        heater.sensors.Add(sensor);
                }
            }

            // turn heaters off for 2 minutes, if temperature is invalid. 
            foreach(var heater in _heaters)
            {
                if (!sensors.Any(x => x.Name == heater.sensors.First().Name))
                {
                    HeaterOff(heater);
                    heater.LastOff = DateTime.Now.AddMinutes(2); // wait 2 minutes before we turn it on again. It will only turn on if it has updated thermocoupler data. 
                }
            }
        }

        public List<double> GetStates()
        {
            return _heaters.Select(x => x.IsOn ? 1.0 : 0.0).ToList();
        }

        public List<double> GetPower()
        {
            return _heaters.Select(x => x.Current).ToList();
        }

        public List<VectorDescriptionItem> GetVectorDescriptionItems()
        {
            var list = _heaters.Select(x => new VectorDescriptionItem("double", x.sensors.First().Name + "_Power", DataTypeEnum.Input)).ToList();
            list.AddRange(_heaters.Select(x => new VectorDescriptionItem("double", x.sensors.First().Name, DataTypeEnum.Output)));
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
