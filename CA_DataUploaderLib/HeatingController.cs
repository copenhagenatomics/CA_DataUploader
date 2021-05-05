using CA.LoopControlPluginBase;
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
        public string Title => "Heating";
        private static int HeaterOnTimeout = 60;
        private bool _running = true;
        private bool _disposed = false;
        private CALogLevel _logLevel = CALogLevel.Normal;
        private readonly List<HeaterElement> _heaters = new List<HeaterElement>();
        private CommandHandler _cmd;
        private SwitchBoardController _switchboardController;
        private readonly OvenCommand _ovenCmd;
        private readonly HeaterCommand _heaterCmd;

        public HeatingController(BaseSensorBox caThermalBox, CommandHandler cmd)
        {
            _cmd = cmd;
            _logLevel = IOconfFile.GetOutputLevel();

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
            cmd.AddCommand("escape", Stop);
            cmd.AddCommand("emergencyshutdown", EmergencyShutdown);     
            var cmdPlugins = new PluginsCommandHandler(cmd);
            _heaterCmd = new HeaterCommand(_heaters);
            _heaterCmd.Initialize(cmdPlugins, new PluginsLogger("heater"));
            _ovenCmd = new OvenCommand(_heaters, oven.Any());
            _ovenCmd.Initialize(cmdPlugins, new PluginsLogger("oven"));
            cmd.Execute("oven off", false); // by executing this, the oven command will ensure the heaters stay off
        }

        private bool EmergencyShutdown(List<string> arg)
        {
            _cmd.Execute("oven off", false);
            return true;
        }

        private bool Stop(List<string> args)
        {
            _running = false;
            return true;
        }

        private void WaitForLoopStopped(object sender, EventArgs args)
        {
            CALog.LogData(LogID.A, "waiting for heaters loop to stop heater actions and turn off the heaters");
            _ovenCmd.ActionsLoopStoppedTask.Wait();
            CALog.LogData(LogID.A, "finished waiting for heaters loop");
        }

        private static void HeaterOff(HeaterElement heater)
        {
            try
            {
                heater.Board().SafeWriteLine($"p{heater._ioconf.PortNumber} off");
            }
            catch (TimeoutException)
            {
                throw new TimeoutException($"Unable to write to {heater.Board().BoxName}");
            }
        }

        private static void HeaterOn(HeaterElement heater)
        {
            try
            {
                heater.Board().SafeWriteLine($"p{heater._ioconf.PortNumber} on {HeaterOnTimeout}");
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
            var values = _switchboardController
                .GetReadInput()
                .Concat(_heaters.Select(x => new SensorSample(x.Name() + "_On/Off", x.IsOn ? 1.0 : 0.0)));
            if (_logLevel == CALogLevel.Debug)
                values = values.Concat(_heaters.Select(x => new SensorSample(x.Name() + "_LoopTime", x.Current.ReadSensor_LoopTime)));

            return values;
        }

        public List<VectorDescriptionItem> GetVectorDescriptionItems()
        {
            var list = _switchboardController.GetReadInputVectorDescriptionItems();
            list.AddRange(_heaters.Select(x => new VectorDescriptionItem("double", x.Name() + "_On/Off", DataTypeEnum.Output)));
            if (_logLevel == CALogLevel.Debug)
                list.AddRange(_heaters.Select(x => new VectorDescriptionItem("double", x.Name() + "_LoopTime", DataTypeEnum.State)));

            CALog.LogInfoAndConsoleLn(LogID.A, $"{list.Count,2} datapoints from HeatingController");
            return list;
        }

        public void Dispose()
        { // class is sealed without unmanaged resources, no need for the full disposable pattern.
            if (_disposed) return;
            _running = false;
            _switchboardController.Dispose();
            _ovenCmd.Dispose();
            _heaterCmd.Dispose();
            _disposed = true;
        }

        // usage: oven 200 220 400
        private class OvenCommand : LoopControlCommand
        {
            public override string Name => "oven";
            public override string ArgsHelp => " [0 - 800] [0 - 800]";
            public override string Description => "where the integer value is the oven temperature top and bottom region";
            private readonly List<HeaterElement> _heaters;
            private CancellationTokenSource _stopTokenSource = new CancellationTokenSource();
            private bool _disposed = false;
            private int _actionLoopStarted = 0;
            private DateTime _startTime;
            private readonly TaskCompletionSource _stopped = new TaskCompletionSource();
            public Task ActionsLoopStoppedTask => _stopped.Task; // task that can be used to wait until this instance has stopped all actions on the ovens.

            public override bool IsHiddenCommand {get; }

            public OvenCommand(List<HeaterElement> heaters, bool hidden)
            {
                _heaters = heaters;
                IsHiddenCommand = hidden;
            }

            protected async override Task Command(List<string> args)
            { 
                if (args.Count < 2)
                {
                    logger.LogError($"Unexpected format: {string.Join(',', args)}. Format: oven temparea1 temparea2 ...");
                    return;
                }

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
                ExecuteCommand($"light main {lightState}");

                await ActionsLoop(); // the actions loop also runs when the oven is off, so that unexpected currents can be detected and repeated off commands issued.
            }

            protected override void Dispose(bool disposing)
            {
                if (_disposed) return;
                if (disposing)
                { // dispose managed state
                    _stopTokenSource.Cancel();
                    _stopTokenSource.Dispose();
                }

                base.Dispose(disposing);

                _disposed = true;
            }

            private async Task ActionsLoop()
            { 
                if (Interlocked.CompareExchange(ref _actionLoopStarted, 1, 0) == 1) 
                    return; // already running
                _startTime = DateTime.UtcNow;
                
                try
                {
                    var token = _stopTokenSource.Token;
                    while (!token.IsCancellationRequested)
                    {
                        try
                        {
                            await SetHeaterInputsFromNextInputVector(token);
                            DoHeatersActions();
                        }
                        catch (Exception ex)
                        {
                            CALog.LogErrorAndConsoleLn(LogID.A, ex.ToString());
                            if (ex is TimeoutException)
                                AllOff();
                        }
                    }

                    CALog.LogInfoAndConsoleLn(LogID.A, "Exiting HeatingController.LoopForever() " + DateTime.UtcNow.Subtract(_startTime).Humanize(5));
                    AllOff();
                }
                finally
                {
                    _stopped.TrySetResult();
                }
            }

            private void DoHeatersActions()
            {
                foreach (var heater in _heaters)
                { // careful consideration must be taken if changing the order of this if/else chain.
                    if (heater.MustTurnOff())
                        HeaterOff(heater);
                    else if (heater.CanTurnOn())
                        HeaterOn(heater);
                    else if (heater.MustResendOnCommand())
                        HeaterOn(heater);
                    else if (heater.MustResendOffCommand())
                        HeaterOff(heater);
                }
            }

            private async Task SetHeaterInputsFromNextInputVector(CancellationToken token)
            {
                var vector = await When(_ => true, token); // act when we get get a new vector (or when we are stopping via the token).
                foreach (var heater in _heaters)
                {
                    if (!vector.TryGetValue(heater._ioconf.CurrentSensorName, out var current))
                        throw new InvalidOperationException($"missing heater current from switchboard controller: {heater._ioconf.CurrentSensorName}");
                    if (!vector.TryGetValue(heater._ioconf.SwitchboardOnOffSensorName, out var switchboardOnOffState))
                        ?? throw new InvalidOperationException($"missing switchboard on/off state from switchboard controller: {heater._ioconf.SwitchboardOnOffSensorName}");
                    heater.Current.SetValueWithoutTimestamp(current);
                    if (vector[heater._ioconf.BoardStateSensorName] == (int)BaseSensorBox.ConnectionState.Connected)
                        heater.Current.TimeStamp = DateTime.UtcNow; // only set when we get fresh values, as it is used to detect stale values in HeaterElement.MustResend*
                    heater.SwitchboardOnState = switchboardOnOffState;
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

            static int ParseTemperature(string t) => int.TryParse(t, out var v) ? v : throw new ArgumentException($"Unexpected target temperature: '{t}'");
        }

        // usage: heater heaterName on
        private class HeaterCommand : LoopControlCommand
        {
            public override string Name => "heater";
            public override string ArgsHelp => " [name] on/off";
            public override string Description => "turn the heater with the given name in IO.conf on and off";
            private readonly List<HeaterElement> _heaters = new List<HeaterElement>();

            public HeaterCommand(List<HeaterElement> heaters)
            {
                _heaters = heaters;
            }

            protected override Task Command(List<string> args)
            { 
                if (args.Count < 3)
                {
                    logger.LogError($"Unexpected format: {string.Join(',', args)}. Format: heater heaterName on");
                    return Task.CompletedTask;
                }

                var name = args[1].ToLower();
                var heater = _heaters.SingleOrDefault(x => x.Name() == name);
                if (heater == null)
                {
                    CALog.LogInfoAndConsoleLn(LogID.A, $"Invalid heater name {name}. Heaters: ${string.Join(',', _heaters.Select(x => x.Name()))}");
                    return Task.CompletedTask; 
                }

                heater.SetManualMode(args[2].ToLower() == "on");
                if (heater.IsOn)
                    HeaterOn(heater);
                else
                    HeaterOff(heater);

                return Task.CompletedTask;
            }

            static int ParseTemperature(string t) => int.TryParse(t, out var v) ? v : throw new ArgumentException($"Unexpected target temperature: '{t}'");
        }
    }
}
