﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace CA_DataUploaderLib
{
    public class HeatingController : CommandHandler, IDisposable
    {
        public int TargetTemperature { get; set; }

        public List<TermoSensor> ValidHatSensors { get; private set; }
        private int _maxHeaterTemperature;
        private bool _running = true;
        private List<HeaterElement> _heaters = new List<HeaterElement>();
        private List<MCUBoard> _switchBoxes;
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

                    Thread.Sleep(1000);
                    if (i++ % 10 == 0)   // check for new termocouplers every 10 seconds. 
                        CheckForNewThermocouplers();
                }
                catch (ArgumentException ex)
                {
                    CALog.LogErrorAndConsole(LogID.A, ex.ToString());
                    _running = false;
                }
                catch (Exception ex)
                {
                    CALog.LogErrorAndConsole(LogID.A, ex.ToString());
                }
            }

            CALog.LogInfoAndConsoleLn(LogID.A, "Exiting LoopHatController.LoopForever()");
            foreach (var heater in _heaters)
                HeaterOff(heater);
        }

        private void AllOff()
        {
            foreach (var box in _switchBoxes)
                box.WriteLine("off");
        }

        private void HeaterOff(HeaterElement heater)
        {
            var box = _switchBoxes.Single(x => x.serialNumber == heater.SwitchBoard);
            box.WriteLine($"p{heater.port} off");
        }

        private void HeaterOn(HeaterElement heater)
        {
            var box = _switchBoxes.Single(x => x.serialNumber == heater.SwitchBoard);
            box.WriteLine($"p{heater.port} on");
        }

        private void CheckForNewThermocouplers()
        {
            var sensors = _caThermalBox.GetAllValidTemperatures();
            var list = sensors.Select(x => x.Heater).Where(x => x != null).ToList();
            // add new heaters
            foreach(var heater in list)
            {
                if (!_heaters.Any(x => x.Name() == heater.Name()))
                    _heaters.Add(heater);
            }

            foreach(var heater in _heaters)
            {
                if (!list.Any(x => x.Name() == heater.Name()))
                {
                    HeaterOff(heater);
                    heater.LastOff = DateTime.Now.AddMinutes(2); // wait 2 minutes before we turn it on again. It will only turn on if it has updated thermocoupler data. 
                }
            }

            VerifyHeatingElementAndThermocouplerMatch(_heaters, _switchBoxes);
        }

        private void VerifyHeatingElementAndThermocouplerMatch(List<HeaterElement> heaters, List<MCUBoard> switchBoxes)
        {
            foreach (var h in heaters)
            {
                if (!switchBoxes.Any(x => x.serialNumber == h.SwitchBoard))
                    throw new ArgumentException($"match for {h.SwitchBoard} in IO.conf not found");
            }
        }

        public List<double> GetStates()
        {
            return _heaters.Select(x => x.IsOn ? 1.0 : 0.0).ToList();
        }

        public bool HandleCommand()
        {
            var cmd = GetCommand();
            if (cmd == null)
                return true;

            if (cmd.Any())
            {
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

                if (cmd.Count() == 1)
                {
                    CALog.LogInfoAndConsoleLn(LogID.A, $"Command: {inputCommand.Replace(Environment.NewLine, "")} - bad command");
                    return true;
                }

                bool commandOK = false;
                if (cmd.First().ToLower() == "oven")
                {
                    int topTemp = GetCmdParam(cmd, 1, 300);
                    int bottomTemp = GetCmdParam(cmd, 2, topTemp);

                    if (topTemp < 900 && bottomTemp < 900)
                    {
                        TargetTemperature = topTemp;
                        _heaters.Where(x => x.Name().ToLower().Contains("bottom")).ToList().ForEach(x => x.OffsetSetTemperature = bottomTemp - topTemp);
                        commandOK = true;
                    }
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
            return _heaters.Select(x => new VectorDescriptionItem("double", x.sensors.First().Name, DataTypeEnum.Output)).ToList();
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
