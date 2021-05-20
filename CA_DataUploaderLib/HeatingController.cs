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
        private bool _disposed = false;
        private readonly List<HeaterElement> _heaters = new List<HeaterElement>();
        private PluginsCommandHandler _cmd;
        private SwitchBoardController _switchboardController;
        private readonly OvenCommand _ovenCmd;
        private readonly HeaterCommand _heaterCmd;
        private readonly CancellationTokenSource _boardLoopsStopTokenSource = new CancellationTokenSource();
        private readonly TaskCompletionSource _boardControlLoopsStopped = new TaskCompletionSource();

        public HeatingController(CommandHandler cmd)
        {
            _cmd = new PluginsCommandHandler(cmd);

            var heatersConfigs = IOconfFile.GetHeater().ToList();
            if (!heatersConfigs.Any())
                return;

            var ovens = IOconfFile.GetOven().ToList();
            foreach (var heater in heatersConfigs)
                _heaters.Add(new HeaterElement(
                    heater, 
                    ovens.SingleOrDefault(x => x.HeatingElement.Name == heater.Name)));

            var unreachableBoards = heatersConfigs.Where(h => h.Map.Board == null).GroupBy(h => h.Map).ToList();
            foreach (var board in unreachableBoards)
                CALog.LogErrorAndConsoleLn(LogID.A, $"Missing board {board.Key} for heaters {string.Join(",", board.Select(h => h.Name))}");
            if (unreachableBoards.Count > 0)
                throw new NotSupportedException("Running with missing heaters is not currently supported");

            _switchboardController = SwitchBoardController.GetOrCreate(cmd);
            _switchboardController.Stopping += WaitForLoopStopped;
            cmd.AddCommand("escape", Stop);
            cmd.AddCommand("emergencyshutdown", EmergencyShutdown);    
            cmd.AddSubsystem(this);
            _heaterCmd = new HeaterCommand(_heaters);
            _heaterCmd.Initialize(new PluginsCommandHandler(cmd), new PluginsLogger("heater"));
            _ovenCmd = new OvenCommand(_heaters, !ovens.Any());
            _ovenCmd.Initialize(new PluginsCommandHandler(cmd), new PluginsLogger("oven"));
            Task.Run(RunHeatersControlLoops);
        }

        private bool EmergencyShutdown(List<string> arg)
        {
            _cmd.Execute("oven off", false);
            return true;
        }

        private bool Stop(List<string> args)
        {
            _boardLoopsStopTokenSource.Cancel();
            return true;
        }

        private void WaitForLoopStopped(object sender, EventArgs args)
        {
            CALog.LogData(LogID.A, "waiting for heaters loop to stop heater actions and turn off the heaters");
            _boardControlLoopsStopped.Task.Wait();
            CALog.LogData(LogID.A, "finished waiting for heaters loop");
        }

        /// <summary>
        /// Gets all the values in the order specified by <see cref="GetVectorDescriptionItems"/>.
        /// </summary>
        public IEnumerable<SensorSample> GetValues() =>
            _heaters.Select(x => new SensorSample(x.Name() + "_On/Off", x.IsOn ? 1.0 : 0.0));

        public List<VectorDescriptionItem> GetVectorDescriptionItems()
        {
            var list = _heaters.Select(x => new VectorDescriptionItem("double", x.Name() + "_On/Off", DataTypeEnum.Output)).ToList();
            CALog.LogInfoAndConsoleLn(LogID.A, $"{list.Count,2} datapoints from HeatingController");
            return list;
        }

        public void Dispose()
        { // class is sealed without unmanaged resources, no need for the full disposable pattern.
            if (_disposed) return;
            _boardLoopsStopTokenSource.Cancel();
            _switchboardController.Dispose();
            _ovenCmd.Dispose();
            _heaterCmd.Dispose();
            _disposed = true;
        }

        private async Task BoardLoop(MCUBoard board, List<HeaterElement> heaters, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var vector = await _cmd.When(_ => true, token);
                    foreach (var heater in _heaters)
                        DoHeaterActions(vector, heater, token);
                }
                catch (Exception ex)
                {
                    CALog.LogErrorAndConsoleLn(LogID.A, ex.ToString());
                }
            }
            
            AllOff(board, heaters);
        }

        private async Task RunHeatersControlLoops()
        {
            DateTime start = DateTime.Now;
            try
            {
                var boardLoops = _heaters
                    .GroupBy(v => v.Board())
                    .Select(g => BoardLoop(g.Key, g.ToList(), _boardLoopsStopTokenSource.Token))
                    .ToList();
                await Task.WhenAll(boardLoops);                
                CALog.LogInfoAndConsoleLn(LogID.A, "Exiting ValveController.LoopForever() " + DateTime.Now.Subtract(start).Humanize(5));
            }
            catch (Exception ex)
            {
                CALog.LogErrorAndConsoleLn(LogID.A, ex.ToString());
            }
            finally
            {
                _boardControlLoopsStopped.TrySetResult();
            }
        }

        private void AllOff(MCUBoard board, List<HeaterElement> heaters)
        {
            try
            {
                heaters.ForEach(x => x.SetTargetTemperature(0));
                board.SafeWriteLine("off"); //TODO: might want to replace with individual control, as switchboard might be shared by valves ...
                CALog.LogInfoAndConsoleLn(LogID.A, "All heaters are off");                
            }
            catch (Exception ex)
            {
                CALog.LogErrorAndConsoleLn(LogID.A, "Error detected while attempting to turn off all heaters", ex);
            }
        }
        
        private void DoHeaterActions(NewVectorReceivedArgs vector, HeaterElement heater, CancellationToken token)
        {
            if (token.IsCancellationRequested)
                return;
            var action = heater.MakeNextActionDecision(vector);
            switch (action)
            {
                case HeaterAction.TurnOn: HeaterOn(heater); break;
                case HeaterAction.TurnOff: HeaterOff(heater); break;
                case HeaterAction.None: break;
                default: throw new ArgumentOutOfRangeException($"Unexpected action received: {action}");
            }
        }

        public static void HeaterOff(HeaterElement heater)
        {
            try
            {
                heater.Board().SafeWriteLine($"p{heater.PortNumber} off");
            }
            catch (TimeoutException)
            {
                throw new TimeoutException($"Unable to write to {heater.Board().BoxName}");
            }
        }

        public static void HeaterOn(HeaterElement heater)
        {
            try
            {
                heater.Board().SafeWriteLine($"p{heater.PortNumber} on {HeaterOnTimeout}");
            }
            catch (TimeoutException)
            {
                throw new TimeoutException($"Unable to write to {heater.Board().BoxName}");
            }
        }

        // usage: oven 200 220 400
        private class OvenCommand : LoopControlCommand
        {
            public override string Name => "oven";
            public override string ArgsHelp => " [0 - 800] [0 - 800]";
            public override string Description => "where the integer value is the oven temperature top and bottom region";
            public override bool IsHiddenCommand {get; }
            private readonly List<HeaterElement> _heaters;

            public OvenCommand(List<HeaterElement> heaters, bool hidden)
            {
                _heaters = heaters;
                IsHiddenCommand = hidden;
            }

            protected override Task Command(List<string> args)
            { 
                if (args.Count < 2)
                {
                    logger.LogError($"Unexpected format: {string.Join(',', args)}. Format: oven temparea1 temparea2 ...");
                    return Task.CompletedTask;
                }

                if (args[1] == "off")
                    _heaters.ForEach(x => x.SetTargetTemperature(0));
                else
                    SetHeatersTargetTemperatures(args.Skip(1).Select(ParseTemperature).ToList());
                var lightState = _heaters.Any(x => x.IsActive) ? "on" : "off";
                ExecuteCommand($"light main {lightState}");
                return Task.CompletedTask;
            }

            private void SetHeatersTargetTemperatures(List<int> temperatures)
            {
                var areas = IOconfFile.GetOven().Select(x => x.OvenArea).Distinct().OrderBy(x => x).ToList();
                if (areas.Count != temperatures.Count && temperatures.Count == 1) // if a single temp was provided, use that for all areas
                    temperatures = temperatures.SelectMany(t => Enumerable.Range(0, areas.Count).Select(_ => t)).ToList();
                else if (areas.Count != temperatures.Count)
                {
                    CALog.LogInfoAndConsoleLn(LogID.A, "Expected oven command format: oven " + string.Join(' ', Enumerable.Range(1, areas.Count).Select(i => $"tempForArea{i}")));
                    throw new ArgumentException($"Arguments did not match the amount of configured areas: {areas.Count}");
                }
                
                var targets = areas.Select((a, i) => (a, temperatures[i])).ToList();
                foreach (var heater in _heaters)
                    heater.SetTargetTemperature(targets);
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
                    HeaterOn(heater); // do this via the heaterElement.MakeDecision
                else
                    HeaterOff(heater);

                return Task.CompletedTask;
            }
        }
    }
}
