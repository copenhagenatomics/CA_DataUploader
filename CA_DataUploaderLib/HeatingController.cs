using CA_DataUploaderLib.IOconf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace CA_DataUploaderLib
{
    public class HeatingController : IDisposable
    {
        public int TargetTemperature { get; set; }

        public List<SensorSample> ValidHatSensors { get; private set; }
        public double Voltage = 230;
        public int HeaterOnTimeout = 60;
        private int _maxHeaterTemperature;
        private bool _running = true;
        private List<HeaterElement> _heaters = new List<HeaterElement>();
        protected List<MCUBoard> _switchBoxes;
        private BaseSensorBox _caThermalBox;
        protected CommandHandler _cmdHandler;
        private int _sendCount = 0;

        public HeatingController(BaseSensorBox caThermalBox, List<MCUBoard> switchBoxes, CommandHandler cmd, int maxHeaterTemperature)
        {
            _caThermalBox = caThermalBox;
            _maxHeaterTemperature = maxHeaterTemperature;
            _switchBoxes = switchBoxes;
            _cmdHandler = cmd;
            TargetTemperature = 0;

            new Thread(() => this.LoopForever()).Start();
            cmd.AddCommand("escape", Stop);
            cmd.AddCommand("help", HelpMenu);
            cmd.AddCommand("oven", Oven);
            cmd.AddCommand("heater", Heater);

            for (int i = 0; i < 20; i++)
            {
                Thread.Sleep(200); // waiting for temperature date to arrive, this ensure only valid heating elements are created. 
                if (_heaters.Any())
                    break; // exit the for loop
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
            int topTemp = CommandHandler.GetCmdParam(args, 1, 300);
            int bottomTemp = CommandHandler.GetCmdParam(args, 2, topTemp);

            if (topTemp < 900 && bottomTemp < 900)
            {
                TargetTemperature = topTemp;
                _heaters.Where(x => x.Name.ToLower().Contains("bottom")).ToList().ForEach(x => x.OffsetSetTemperature = bottomTemp - topTemp);
                if (TargetTemperature > 0)
                    _cmdHandler.Execute("light on");
                else
                    _cmdHandler.Execute("light off");
                return true;
            }

            return false;
        }

        public bool Heater(List<string> args)
        {
            var heater = _heaters.SingleOrDefault(x => x.Name.ToLower() == args[1].ToLower());
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
                    int maxTemperature = Math.Min(TargetTemperature, _maxHeaterTemperature);
                    foreach (var heater in _heaters)
                    {
                        if (heater.IsOn && heater.MustTurnOff(maxTemperature))
                        {
                            heater.LastOff = DateTime.UtcNow;
                            heater.IsOn = false;
                            HeaterOff(heater);
                        }
                        else if (!heater.IsOn && heater.CanTurnOn(maxTemperature))
                        {
                            heater.LastOn = DateTime.UtcNow;
                            heater.IsOn = true;
                            HeaterOn(heater);
                        }
                    }

                    foreach (var box in _switchBoxes)
                    {
                        var values = SwitchBoardBase.ReadInputFromSwitchBoxes(box);
                        GetCurrentValues(box.PortName, values);
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

        private void GetCurrentValues(string USBPort, List<double> values)
        {
            if (values.Count() > 0)
            {
                foreach (var heater in _heaters.Where(x => x.USBPort == USBPort))
                {
                    heater.Current = values[heater.PortNumber - 1];

                    // this is a hot fix to make sure heaters are on/off. 
                    if (_sendCount++ == 10)
                    {
                        _sendCount = 0;
                        if (heater.Current == 0 && heater.IsOn)
                        {
                            CALog.LogInfoAndConsole(LogID.B, "_");
                            HeaterOn(heater);
                        }

                        if (heater.Current > 0 && !heater.IsOn)
                        {
                            CALog.LogInfoAndConsole(LogID.B, "_");
                            HeaterOff(heater);
                        }
                    }
                }
            }
        }

        private void AllOff()
        {
            foreach (var box in _switchBoxes)
                box.SafeWriteLine("off");

            CALog.LogInfoAndConsoleLn(LogID.A, "All heaters are off");
        }

        protected virtual void HeaterOff(HeaterElement heater)
        {
            MCUBoard box = null;
            try
            {
                box = _switchBoxes.Single(x => x.PortName == heater.USBPort);
                box.SafeWriteLine($"p{heater.PortNumber} off");
                CALog.LogInfoAndConsoleLn(LogID.B, heater.ToString());
            }
            catch (TimeoutException)
            {
                throw new TimeoutException($"Unable to write to {box.PortName} {box.productType}");
            }
        }

        protected virtual void HeaterOn(HeaterElement heater)
        {
            MCUBoard box = null;
            try
            {
                box = _switchBoxes.Single(x => x.PortName == heater.USBPort);
                box.SafeWriteLine($"p{heater.PortNumber} on {HeaterOnTimeout}");
                CALog.LogInfoAndConsoleLn(LogID.B, heater.ToString());
            }
            catch (TimeoutException)
            {
                throw new TimeoutException($"Unable to write to {box.PortName} {box.productType}");
            }
        }

        private void CheckForNewThermocouplers()
        {
            var sensors = _caThermalBox.GetAllValidDatapoints();
            var sensorsAttachedHeaters = sensors.Select(x => x.Heater).Where(x => x != null).ToList();
            
            // add new heaters
            foreach(var heater in sensorsAttachedHeaters)
            {
                if (!_heaters.Any(x => x.Name == heater.Name) && _switchBoxes.Any(x => x.PortName == heater.USBPort))
                    _heaters.Add(heater);
            }

            // turn heaters off for 2 minutes, if temperature is invalid. 
            foreach(var heater in _heaters)
            {
                if (!sensorsAttachedHeaters.Any(x => x.Name == heater.Name))
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
            var list = _heaters.Select(x => new VectorDescriptionItem("double", x.sensors.First().Name + "_Power", DataTypeEnum.Input)).
                Concat(_heaters.Select(x => new VectorDescriptionItem("double", x.sensors.First().Name, DataTypeEnum.Output))).ToList();
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
