using CA.LoopControlPluginBase;
using CA_DataUploaderLib.Extensions;
using CA_DataUploaderLib.IOconf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CA_DataUploaderLib
{
    public sealed class HeatingController : IDisposable, ISubsystemWithVectorData
    {
        public string Title => "Heaters";
        private bool _disposed = false;
        private readonly List<HeaterElement> _heaters = new List<HeaterElement>();
        private readonly PluginsCommandHandler _cmd;
        private readonly SwitchBoardController _switchboardController;
        private readonly OvenCommand _ovenCmd;
        private readonly HeaterCommand _heaterCmd;
        private readonly CommandHandler _cmdUnwrapped;

        public HeatingController(CommandHandler cmd)
        {
            _cmd = new PluginsCommandHandler(cmd);
            _cmdUnwrapped = cmd;

            var heatersConfigs = IOconfFile.GetHeater().ToList();
            if (!heatersConfigs.Any())
                return;

            var ovens = IOconfFile.GetOven().ToList();
            foreach (var heater in heatersConfigs)
                _heaters.Add(new HeaterElement(
                    heater, 
                    ovens.SingleOrDefault(x => x.HeatingElement.Name == heater.Name)));

            cmd.AddCommand("emergencyshutdown", EmergencyShutdown);    
            cmd.AddSubsystem(this);
            _heaterCmd = new HeaterCommand(_heaters);
            _heaterCmd.Initialize(new PluginsCommandHandler(cmd), new PluginsLogger("heater"));
            _ovenCmd = new OvenCommand(_heaters, !ovens.Any());
            _ovenCmd.Initialize(new PluginsCommandHandler(cmd), new PluginsLogger("oven"));
            _switchboardController = SwitchBoardController.GetOrCreate(cmd);
        }

        private bool EmergencyShutdown(List<string> arg)
        {
            _cmd.Execute("oven off", false);
            return true;
        }

        public Task Run(CancellationToken token)
        {
            foreach (var heater in _heaters)
                heater.ReportDetectedConfigAlerts(_cmdUnwrapped);

            return _switchboardController.Run(token);
        }

        public IEnumerable<SensorSample> GetInputValues() => Enumerable.Empty<SensorSample>();
        public List<VectorDescriptionItem> GetVectorDescriptionItems() => 
            _heaters.SelectMany(x => SwitchboardAction.GetVectorDescriptionItems(x.Name())).ToList();
        public IEnumerable<SensorSample> GetDecisionOutputs(NewVectorReceivedArgs inputVectorReceivedArgs)
        { 
            foreach (var heater in _heaters)
            foreach (var sample in heater.MakeNextActionDecision(inputVectorReceivedArgs).ToVectorSamples(heater.Name(), inputVectorReceivedArgs.GetVectorTime()))
                yield return sample;
        }

        public void Dispose()
        { // class is sealed without unmanaged resources, no need for the full disposable pattern.
            if (_disposed) return;
            _switchboardController?.Dispose();
            _ovenCmd?.Dispose();
            _heaterCmd?.Dispose();
            _disposed = true;
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

                var name = args[1];
                var heater = _heaters.SingleOrDefault(x => x.Name() == name);
                if (heater == null)
                {
                    CALog.LogInfoAndConsoleLn(LogID.A, $"Invalid heater name {name}. Heaters: ${string.Join(',', _heaters.Select(x => x.Name()))}");
                    return Task.CompletedTask; 
                }

                heater.SetManualMode(args[2].ToLower() == "on");
                return Task.CompletedTask;
            }
        }
    }
}
