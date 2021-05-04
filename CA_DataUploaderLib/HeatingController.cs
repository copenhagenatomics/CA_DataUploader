using CA_DataUploaderLib.IOconf;
using Humanizer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CA_DataUploaderLib
{

    public sealed class HeatingController : IDisposable, ISubsystemWithVectorData
    {
        public int HeaterOnTimeout = 60;
        public string Title => "Heating";
        private bool _running = true;
        private CALogLevel _logLevel = CALogLevel.Normal;
        private double _offTemperature = 0;
        private double _lastTemperature = 0;
        private DateTime _startTime;
        private readonly List<HeaterElement> _heaters = new List<HeaterElement>();
        private CommandHandler _cmd;
        private SwitchBoardController _switchboardController;        
        private readonly TaskCompletionSource _stopped = new TaskCompletionSource();

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
                int area = oven.SingleOrDefault(x => x.HeatingElement.Name == heater.Name && x.OvenArea > 0)?.OvenArea ?? -1;
                _heaters.Add(new HeaterElement(area, heater, sensors.Where(x => x.Input.Name == ovenSensor)));
            }

            if (!_heaters.Any())
                return;

            var unreachableBoards = heaters.Where(h => h.Map.Board == null).GroupBy(h => h.Map).ToList();
            foreach (var board in unreachableBoards)
                CALog.LogErrorAndConsoleLn(LogID.A, $"Missing board {board.Key} for heaters {string.Join(",", board.Select(h => h.Name))}");
            if (unreachableBoards.Count > 0)
                throw new NotSupportedException("Running with missing heaters is not currently supported");

            _switchboardController = SwitchBoardController.GetOrCreate(cmd);
            _switchboardController.Stopping += WaitForLoopStopped;
            new Thread(() => this.LoopForever()).Start();
            cmd.AddCommand("escape", Stop);
            Thread.Sleep(200); // avoid heater actions before we get the temperature data
            cmd.AddCommand("help", HelpMenu);
            cmd.AddCommand("emergencyshutdown", EmergencyShutdown);
            cmd.AddCommand("heater", Heater);
            if (oven.Any())
                cmd.AddCommand("oven", Oven);
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
                _heaters.ForEach(x => x.SetTemperature(0));
            else
            {
                var areas = IOconfFile.GetOven().Select(x => x.OvenArea).Distinct().OrderBy(x => x).ToList();
                var tempArgs = args.Skip(1).ToList();
                if (areas.Count != tempArgs.Count && tempArgs.Count == 1) // if a single temp was provided, use that for all areas
                    tempArgs = tempArgs.SelectMany(t => Enumerable.Range(0, areas.Count).Select(_ => t)).ToList();
                else if (areas.Count != tempArgs.Count) 
                {
                    CALog.LogInfoAndConsoleLn(LogID.A, "Expected oven command format: oven " + string.Join(' ', Enumerable.Range(1, areas.Count).Select(i => $"tempForArea{i}")));
                    throw new ArgumentException($"Arguments did not match the amount of configured areas: {areas.Count}");
                }

                var targets = tempArgs
                    .Select(ParseTemperature)
                    .SelectMany((t, i) => _heaters.Where(x => x.IsArea(areas[i])).Select(h => (h, t)));
                foreach (var (heater, temperature) in targets)
                    heater.SetTemperature(temperature);
            }

            var lightState = _heaters.Any(x => x.IsActive) ? "on" : "off";
            _cmd.Execute($"light main {lightState}", false);
            return true;
        }

        static int ParseTemperature(string t) =>
            int.TryParse(t, out var v) ? v : throw new ArgumentException($"Unexpected target temperature: '{t}'");

        public bool Heater(List<string> args)
        {
            var name = args[1].ToLower();
            var heater = _heaters.SingleOrDefault(x => x.Name() == name);
            if (heater == null)
            {
                CALog.LogInfoAndConsoleLn(LogID.A, $"Invalid heater name {name}. Heaters: ${string.Join(',', _heaters.Select(x => x.Name()))}");
                return false; 
            }

            heater.SetManualMode(args[2].ToLower() == "on");
            if (heater.IsOn)
                HeaterOn(heater);
            else
                HeaterOff(heater);

            return true;
        }

        private void LoopForever()
        {
            _startTime = DateTime.UtcNow;
            _logLevel = IOconfFile.GetOutputLevel();
            
            while (_running)
            {
                try
                {
                    Thread.Sleep(100); // we wait before the actions, so that errors are also throttled.
                    SetHeatersInputs();
                    DoHeatersActions();
                }
                catch (Exception ex)
                {
                    CALog.LogErrorAndConsoleLn(LogID.A, ex.ToString());
                    if (ex is TimeoutException)
                        AllOff();
                }
            }

            try
            {
                CALog.LogInfoAndConsoleLn(LogID.A, "Exiting HeatingController.LoopForever() " + DateTime.UtcNow.Subtract(_startTime).Humanize(5));
                AllOff();
            }
            finally
            {
                _stopped.TrySetResult();
            }
        }

        private void WaitForLoopStopped(object sender, EventArgs args)
        {
            CALog.LogData(LogID.A, "waiting for heaters loop to stop heater actions and turn off the heaters");
            _stopped.Task.Wait();
            CALog.LogData(LogID.A, "finished waiting for heaters loop");
        }

        private void DoHeatersActions()
        {
            foreach (var heater in _heaters)
            { // careful consideration must be taken if changing the order of this if/else chain.
                if (heater.MustTurnOff())
                {
                    _offTemperature = heater.MaxSensorTemperature();
                    _lastTemperature = heater.lastTemperature;
                    HeaterOff(heater);
                }
                else if (heater.CanTurnOn())
                    HeaterOn(heater);
                else if (heater.MustResendOnCommand())
                    HeaterOn(heater);
                else if (heater.MustResendOffCommand())
                    HeaterOff(heater);
            }
        }

        private void SetHeatersInputs()
        {
            var values = _switchboardController.GetReadInput().ToArray();
            foreach (var heater in _heaters)
            {
                var current = values.SingleOrDefault(s => s.Name == heater._ioconf.CurrentSensorName)
                    ?? throw new InvalidOperationException($"missing heater current from switchboard controller: {heater._ioconf.CurrentSensorName}");
                var switchboardOnOffState = values.SingleOrDefault(s => s.Name == heater._ioconf.SwitchboardOnOffSensorName)
                    ?? throw new InvalidOperationException($"missing switchboard on/off state from switchboard controller: {heater._ioconf.SwitchboardOnOffSensorName}");
                heater.Current.SetValueWithoutTimestamp(current.Value);
                heater.Current.TimeStamp = current.TimeStamp; // keeping the original timestamp as it can be used to detect stale values in the HeaterElement MustResend* logic
                heater.SwitchboardOnState = switchboardOnOffState.Value;
            }
        }

        private void AllOff()
        {
            try
            {
                _heaters.ForEach(x => x.SetTemperature(0));
                foreach (var box in _heaters.Select(x => x.Board()).Where(x => x != null).Distinct())
                    box.SafeWriteLine("off");

                CALog.LogInfoAndConsoleLn(LogID.A, "All heaters are off");                
            }
            catch (Exception ex)
            {
                CALog.LogErrorAndConsoleLn(LogID.A, "Error detected while attempting to turn off all heaters", ex);
            }
        }

        private void HeaterOff(HeaterElement heater)
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

        private void HeaterOn(HeaterElement heater)
        {
            try
            {
                heater.LastOn = DateTime.UtcNow;
                heater.Board().SafeWriteLine($"p{heater._ioconf.PortNumber} on {HeaterOnTimeout}");
                if (_logLevel == CALogLevel.Debug)
                    CALog.LogData(LogID.B, $"wrote p{heater._ioconf.PortNumber} on {HeaterOnTimeout} to {heater.Board().BoxName}");
            }
            catch (TimeoutException)
            {
                throw new TimeoutException($"Unable to write to {heater.Board().BoxName}");
            }
        }

        /// <summary>
        /// Gets all the values in the order specified by <see cref="GetVectorDescriptionItems"/>.
        /// </summary>
        public IEnumerable<SensorSample> GetValues()
        {
            var currentValues = _heaters.Select(x => x.Current.Clone());
            var states = _heaters.Select(x => new SensorSample(x.Name() + "_On/Off", x.IsOn ? 1.0 : 0.0));
            var switchboardStates = _heaters.Select(x => new SensorSample(x._ioconf.SwitchboardOnOffSensorName, x.SwitchboardOnState));
            var values = currentValues.Concat(states).Concat(switchboardStates);
            if (_logLevel == CALogLevel.Debug)
            {
                var loopTimes = _heaters.Select(x => new SensorSample(x.Name() + "_LoopTime", x.Current.ReadSensor_LoopTime));
                return values
                    .Concat(loopTimes)
                    .Append(new SensorSample("off_temperature", _offTemperature))
                    .Append(new SensorSample("last_temperature", _lastTemperature));
            }

            return values;
        }

        public List<VectorDescriptionItem> GetVectorDescriptionItems()
        {
            var list = _heaters.Select(x => new VectorDescriptionItem("double", x.Current.Name, DataTypeEnum.Input)).ToList();
            list.AddRange(_heaters.Select(x => new VectorDescriptionItem("double", x.Name() + "_On/Off", DataTypeEnum.Output)));
            list.AddRange(_heaters.Select(x => new VectorDescriptionItem("double", x._ioconf.SwitchboardOnOffSensorName, DataTypeEnum.Output)));
            if (_logLevel == CALogLevel.Debug)
            {
                list.AddRange(_heaters.Select(x => new VectorDescriptionItem("double", x.Name() + "_LoopTime", DataTypeEnum.State)));
                list.Add(new VectorDescriptionItem("double", "off_temperature", DataTypeEnum.State));
                list.Add(new VectorDescriptionItem("double", "last_temperature", DataTypeEnum.State));
            }

            CALog.LogInfoAndConsoleLn(LogID.A, $"{list.Count,2} datapoints from HeatingController");
            return list;
        }

        public void Dispose()
        { // class is sealed without unmanaged resources, no need for the full disposable pattern.
            _running = false;
        }
    }
}
