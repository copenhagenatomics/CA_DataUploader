using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace CA_DataUploaderLib
{
    public class HeatingController : CommandHandler, IDisposable
    {
        public int TargetTemperature { get; set; }

        public List<TermoSensor> ValidHatSensors { get; private set; }
        public double Voltage = 230;
        public int HeaterOnTimeout = 60;
        private int _maxHeaterTemperature;
        private bool _running = true;
        private List<HeaterElement> _heaters = new List<HeaterElement>();
        protected List<MCUBoard> _switchBoxes;
        private CAThermalBox _caThermalBox;

        public HeatingController(CAThermalBox caThermalBox, List<MCUBoard> switchBoxes, int maxHeaterTemperature)
        {
            _caThermalBox = caThermalBox;
            _maxHeaterTemperature = maxHeaterTemperature;
            _switchBoxes = switchBoxes;
            TargetTemperature = 0;

            new Thread(() => this.LoopForever()).Start();

            for (int i = 0; i < 20; i++)
            {
                Thread.Sleep(200); // waiting for temperature date to arrive, this ensure only valid heating elements are created. 
                if (_heaters.Any())
                    break; // exit the for loop
            }
        }

        private void LoopForever()
        {
            int i = 0;
            DateTime start = DateTime.Now;
            var logLevel = IOconf.GetOutputLevel();
            while (_running)
            {
                try
                {
                    int maxTemperature = Math.Min(TargetTemperature, _maxHeaterTemperature);
                    foreach (var heater in _heaters)
                    {
                        if (heater.IsOn && heater.MustTurnOff(maxTemperature))
                        {
                            HeaterOff(heater);
                            heater.LastOff = DateTime.UtcNow;
                            heater.IsOn = false;
                            CALog.LogInfoAndConsoleLn(LogID.B, heater.ToString());
                        }
                        else if (!heater.IsOn && heater.CanTurnOn(maxTemperature))
                        {
                            HeaterOn(heater);
                            heater.LastOn = DateTime.UtcNow;
                            heater.IsOn = true;
                            CALog.LogInfoAndConsoleLn(LogID.B, heater.ToString());
                        }
                    }

                    ReadInputFromSwitchBoxes();

                    Thread.Sleep(500);
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
                    _heaters.Clear();
                }
                catch (Exception ex)
                {
                    CALog.LogErrorAndConsole(LogID.A, ex.ToString());
                }
            }

            CALog.LogInfoAndConsoleLn(LogID.A, "Exiting LoopHatController.LoopForever() " + DateTime.Now.Subtract(start).TotalSeconds.ToString() + " seconds");
            foreach (var heater in _heaters)
                HeaterOff(heater);
        }

        private const string _SwitchBoxPattern = "P1=(\\d\\.\\d\\d)A; P2=(\\d\\.\\d\\d)A; P3=(\\d\\.\\d\\d)A; P4=(\\d\\.\\d\\d)A;";

        private void ReadInputFromSwitchBoxes()
        {
            foreach (var box in _switchBoxes)
            {
                var line = box.ReadExisting();
                var match = Regex.Match(line, _SwitchBoxPattern);
                if (match.Success)
                {
                    GetCurrentValues(box.serialNumber, match);
                }
            }
        }

        private void GetCurrentValues(string serialNumber, Match match)
        {
            double dummy;
            var values = match.Groups.Cast<Group>().Skip(1)
                .Where(x => double.TryParse(x.Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out dummy))
                .Select(x => double.Parse(x.Value, CultureInfo.InvariantCulture)).ToArray();

            if (values.Count() == 4)
            {
                foreach (var heater in _heaters.Where(x => x.SwitchBoard == serialNumber))
                {
                    heater.Current = values[heater.port - 1];
                    if (heater.Current == 0 && heater.IsOn) HeaterOn(heater);
                    if (heater.Current > 0 && !heater.IsOn) HeaterOff(heater);
                }
            }
        }

        private void AllOff()
        {
            foreach (var box in _switchBoxes)
                box.WriteLine("off");

            CALog.LogInfoAndConsoleLn(LogID.A, "All heaters are off");
        }

        protected virtual void HeaterOff(HeaterElement heater)
        {
            MCUBoard box = null;
            try
            {
                box = _switchBoxes.Single(x => x.serialNumber == heater.SwitchBoard);
                box.WriteLine($"p{heater.port} off");
            }
            catch (TimeoutException)
            {
                throw new TimeoutException($"Unable to write to {box.serialNumber} {box.productType}");
            }
        }

        protected virtual void HeaterOn(HeaterElement heater)
        {
            MCUBoard box = null;
            try
            {
                box = _switchBoxes.Single(x => x.serialNumber == heater.SwitchBoard);
                box.WriteLine($"p{heater.port} on {HeaterOnTimeout}");
            }
            catch (TimeoutException)
            {
                throw new TimeoutException($"Unable to write to {box.serialNumber} {box.productType}");
            }
        }

        protected virtual void Light(bool on)
        {
            // do nothing, you can override. 
        }

        private void CheckForNewThermocouplers()
        {
            var sensors = _caThermalBox.GetAllValidTemperatures();
            var sensorsAttachedHeaters = sensors.Select(x => x.Heater).Where(x => x != null).ToList();
            
            // add new heaters
            foreach(var heater in sensorsAttachedHeaters)
            {
                if (!_heaters.Any(x => x.Name() == heater.Name()) && _switchBoxes.Any(x => x.serialNumber == heater.SwitchBoard))
                    _heaters.Add(heater);
            }

            // turn heaters off for 2 minutes, if temperature is invalid. 
            foreach(var heater in _heaters)
            {
                if (!sensorsAttachedHeaters.Any(x => x.Name() == heater.Name()))
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
            return _heaters.Select(x => x.Current*Voltage).ToList();
        }

        public bool HandleCommand()
        {
            var cmd = GetCommand();
            if (cmd == null)
                return true;

            if (cmd.Any())
            {
                inputCommand = string.Empty;
                switch (cmd.First().ToLower())
                {
                    case "escape":
                        AllOff();
                        return false;
                    case "stop":
                        AllOff();
                        return true;
                    case "help":
                        HelpMenu();
                        return true;
                }

                //if (_cmdParser.TryExecute(cmd))
                //    return true;

                bool commandOK = false;
                if (cmd.First().ToLower() == "oven")
                {
                    int topTemp = GetCmdParam(cmd, 1, 300);
                    int bottomTemp = GetCmdParam(cmd, 2, topTemp);

                    if (topTemp < 900 && bottomTemp < 900)
                    {
                        TargetTemperature = topTemp;
                        _heaters.Where(x => x.Name().ToLower().Contains("bottom")).ToList().ForEach(x => x.OffsetSetTemperature = bottomTemp - topTemp);
                        Light(TargetTemperature > 0);
                        commandOK = true;
                    }
                }

                if(cmd.First().ToLower() == "light")
                {
                    if (cmd[1] == "on") { Light(true); commandOK = true; }
                    if (cmd[1] == "off") { Light(false); commandOK = true; }
                }

                if (commandOK)
                    CALog.LogInfoAndConsoleLn(LogID.A, $"Command: {string.Join(" ", cmd)} - command accepted");
                else
                    CALog.LogInfoAndConsoleLn(LogID.A, $"Command: {string.Join(" ", cmd)} - bad command");
            }
            else
            {
                CALog.LogInfoAndConsoleLn(LogID.A, $"Command: {inputCommand.Replace(Environment.NewLine, "")} - bad command");
            }

            inputCommand = string.Empty;
            return true;
        }

        private void HelpMenu()
        {
            CALog.LogInfoAndConsoleLn(LogID.A, "Commands: ");
            CALog.LogInfoAndConsoleLn(LogID.A, $"oven [0 - 800] [0 - 800]  - where the integer value is the oven temperature top and bottom region");
            CALog.LogInfoAndConsoleLn(LogID.A, $"stop                      - stop the pump and power supply and turn off all power");
            CALog.LogInfoAndConsoleLn(LogID.A, $"help                      - print the full list of available commands");

            inputCommand = string.Empty;
        }

        private int GetCmdParam(List<string> cmd, int index, int defaultValue)
        {
            if (cmd.Count() > index)
            {
                int value;
                if (int.TryParse(cmd[index], out value))
                    return value;
            }

            return defaultValue;
        }

        public List<VectorDescriptionItem> GetVectorDescriptionItems()
        {
            return _heaters.Select(x => new VectorDescriptionItem("double", x.sensors.First().Name+"_Power", DataTypeEnum.Input)).
                Concat(_heaters.Select(x => new VectorDescriptionItem("double", x.sensors.First().Name, DataTypeEnum.Output))).ToList();
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
