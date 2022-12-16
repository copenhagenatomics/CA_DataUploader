#nullable enable
using CA.LoopControlPluginBase;
using CA_DataUploaderLib.Extensions;
using CA_DataUploaderLib.IOconf;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace CA_DataUploaderLib
{
    public sealed class HeatingController
    {
        public HeatingController(CommandHandler cmd)
        {
            var heatersConfigs = IOconfFile.GetHeater().ToList();
            if (!heatersConfigs.Any())
                return;

            var ovens = IOconfFile.GetOven().ToList();
            var allAreas = IOconfFile.GetOven().Select(x => x.OvenArea).Distinct();

            //notice that the decisions states and outputs are handled by the registered decisions, while the switchboard inputs and actuations are handled by the switchboard controller
            cmd.AddDecisions(allAreas.Select(a => new OvenAreaDecision(new($"ovenarea{a}", a))).ToList());
            cmd.AddDecisions(heatersConfigs.Select(h => new HeaterDecision(h, ovens.SingleOrDefault(x => x.HeatingElement.Name == h.Name))).ToList());
            SwitchBoardController.Initialize(cmd);
        }

        public class HeaterDecision : LoopControlDecision
        {
            public enum States { initial, WaitingControlPeriod, ManualOn, InControlPeriod, TemperatureBoardsDisconnected, NegativeOrZeroTemperature, TemperatureReadError, WaitingManualCommand }
            public enum Events { none, vector, heateroff, heateron, emergencyshutdown, oven };
            private readonly Dictionary<string, Events> _eventsMap;
            private readonly Config _config;
            private Indexes? _indexes;
            public override string Name => _config.Name;
            public override PluginField[] PluginFields => new PluginField[] { 
                $"{Name}_state", ($"{Name}_onoff", FieldType.Output), new($"{Name}_nextcontrolperiod") { Upload = false }, new($"{Name}_controlperiodtimeoff") { Upload = false} };
            public override string[] HandledEvents => new List<string>(_eventsMap.Keys).ToArray();
            public HeaterDecision(IOconfHeater heater, IOconfOven? oven) : this(ToConfig(heater, oven)) { }
            public HeaterDecision(Config config)
            {
                _config = config;
                _eventsMap = new(StringComparer.OrdinalIgnoreCase) { { $"heater {Name} on", Events.heateron }, { $"heater {Name} off", Events.heateroff }, { "emergencyshutdown", Events.emergencyshutdown }, { "oven", Events.oven } };
            }

            public override void Initialize(CA.LoopControlPluginBase.VectorDescription desc) => _indexes = new(desc, _config);
            public override void MakeDecision(CA.LoopControlPluginBase.DataVector vector, List<string> events)
            {
                if (_indexes == null) throw new InvalidOperationException("Unexpected call to MakeDecision before Initialize was called first");
                var model = new Model(vector, _indexes, _config);

                foreach (var @event in events)
                {
                    if (_eventsMap.TryGetValue(@event.StartsWith("oven", StringComparison.OrdinalIgnoreCase) ? "oven" : @event, out var e)) //note the oven prefix also reacts to ovenarea commands
                        model.MakeDecision(e);
                }

                model.MakeDecision(Events.vector);
            }

            private static Config ToConfig(IOconfHeater heater, IOconfOven? oven)
            {
                if (oven == null)
                {
                    CALog.LogInfoAndConsoleLn(LogID.A, $"Warn: no oven configured for heater {heater.Name}");
                    return new(heater.Name, new List<string>().AsReadOnly());//notice we don't set the maxtemperature configured at the heater level, as there is no way to enforce as the oven config owns the temperature sensor
                }

                return new(heater.Name, oven.BoardStateSensorNames)
                {
                    MaxTemperature = heater.MaxTemperature,
                    Area = oven.OvenArea,
                    OvenSensor = oven.TemperatureSensorName,
                    ProportionalGain = oven.ProportionalGain,
                    ControlPeriod = oven.ControlPeriod,
                    MaxOutputPercentage = oven.MaxOutputPercentage
                };
            }

            public class Config
            {
                public Config(string name, ReadOnlyCollection<string> temperatureBoardStateSensorNames)
                {
                    Name = name;
                    TemperatureBoardStateSensorNames = temperatureBoardStateSensorNames;
                }
                public int Area { get; init; } = -1; // -1 when not set
                public string AreaName => $"ovenarea{Area}_target";
                public string? OvenSensor { get; init; } // sensor inside the oven somewhere.
                public int MaxTemperature { get; init; }
                public string Name { get; }
                public ReadOnlyCollection<string> TemperatureBoardStateSensorNames { get; }
                public double ProportionalGain { get; init; }
                public TimeSpan ControlPeriod { get; init; }
                public double MaxOutputPercentage { get; init; }
            }

#pragma warning disable IDE1006 // Naming Styles - decisions are coded using a similar approach to decisions plugins, which avoid casing rules in properties to more have naming more similar to the original fields
            public ref struct Model
            {
                private readonly Indexes _indexes;
                private readonly Config _config;
                private readonly CA.LoopControlPluginBase.DataVector _latestVector;

                public Model(CA.LoopControlPluginBase.DataVector latestVector, Indexes indexes, Config config)
                {
                    _latestVector = latestVector;
                    _indexes = indexes;
                    _config = config;
                }
                public States State { get => (States)_latestVector[_indexes.state]; set => _latestVector[_indexes.state] = (int)value; }
                public double ovensensor { get => _latestVector[_indexes.ovensensor]; set => _latestVector[_indexes.ovensensor] = value; }
                public bool ovensensor_defined => _indexes.ovensensor > -1;
                public double target { get => _latestVector[_indexes.target]; set => _latestVector[_indexes.target] = value; }
                public bool target_defined => _indexes.target > -1;
                public double on { get => _latestVector[_indexes.on]; set => _latestVector[_indexes.on] = value; }
                public double nextcontrolperiod { get => _latestVector[_indexes.nextcontrolperiod]; set => _latestVector[_indexes.nextcontrolperiod] = value; }
                public double controlperiodtimeoff { get => _latestVector[_indexes.controlperiodtimeoff]; set => _latestVector[_indexes.controlperiodtimeoff] = value; }
                public bool TemperatureBoardsConnected
                {
                    get
                    {
                        foreach (var board in _indexes.TemperatureBoardsStates)
                        {
                            if (_latestVector[board] != (int)BaseSensorBox.ConnectionState.ReceivingValues)
                                return false;
                        }

                        return true;
                    }
                }

                internal void MakeDecision(Events e)
                {
                    var oldState = State;
                    State = (oldState, e) switch
                    {
                        (States.initial, _) => States.WaitingControlPeriod,
                        (_, Events.heateron) => States.ManualOn,
                        (States.ManualOn, Events.emergencyshutdown) => States.WaitingControlPeriod, //manual on is the only state not controlled by the oven area which must already set to 0 by OvenAreaDecision
                        (States.ManualOn, Events.heateroff) => States.WaitingControlPeriod,
                        //Note that direct changes by plugins to the oven area target changes are only being applied on the next control period (requires being able to detect the target changed since our last decision which requires an extra field).
                        (States.WaitingControlPeriod, _) when !target_defined || !ovensensor_defined => States.WaitingManualCommand,
                        (States.WaitingControlPeriod, _) when !TemperatureBoardsConnected => States.TemperatureBoardsDisconnected,
                        (States.WaitingControlPeriod, _) when ovensensor <= 0 => States.NegativeOrZeroTemperature,
                        (States.WaitingControlPeriod, _) when ovensensor >= 10000 => States.TemperatureReadError,
                        (States.WaitingControlPeriod, _) when target > 0 => States.InControlPeriod,
                        (States.TemperatureBoardsDisconnected, _) when TemperatureBoardsConnected => States.WaitingControlPeriod,
                        (States.NegativeOrZeroTemperature, _) when ovensensor > 0 => States.WaitingControlPeriod,
                        (States.TemperatureReadError, _) when ovensensor < 10000 => States.WaitingControlPeriod,
                        (States.InControlPeriod, _) when _latestVector.Reached(nextcontrolperiod) => States.WaitingControlPeriod,
                        (States.InControlPeriod, _) when target == 0 => States.WaitingControlPeriod,
                        (var s, _) => s //no transition by default
                    };
                    if (oldState != State)
                    {
                        for (int iterationsToParent = 0; iterationsToParent < 1; iterationsToParent++)
                        {
                            switch (oldState, State, iterationsToParent)
                            {
                                case (States.InControlPeriod, _, 0):
                                    controlperiodtimeoff = 0;
                                    nextcontrolperiod = 0;
                                    break;
                                default: //any state without exit actions goes here
                                    break;
                            }
                        }

                        switch (State)
                        {
                            case States.WaitingControlPeriod or States.TemperatureBoardsDisconnected or States.NegativeOrZeroTemperature or States.TemperatureReadError:
                                on = 0;
                                break;
                            case States.ManualOn:
                                on = 1;
                                break;
                            case States.InControlPeriod:
                                var secondsOn = Math.Min(
                                    (Math.Min(target, _config.MaxTemperature) - ovensensor) * _config.ProportionalGain,
                                    _config.MaxOutputPercentage * _config.ControlPeriod.TotalSeconds
                                    );
                                nextcontrolperiod = _latestVector.TimeAfter((int)_config.ControlPeriod.TotalMilliseconds);
                                controlperiodtimeoff = _latestVector.TimeAfter(secondsOn < 0.1d ? 0 : (int)(secondsOn * 1000));
                                on = _latestVector.Reached(controlperiodtimeoff) ? 0 : 1;
                                break;
                            default: //any state without entry actions goes here
                                break;
                        }
                    }
                    if (oldState == State)
                    {
                        switch ((State, e))
                        {
                            case (States.WaitingControlPeriod or States.TemperatureBoardsDisconnected or States.NegativeOrZeroTemperature or States.TemperatureReadError, Events.vector):
                                on = 0;
                                break;
                            case (States.ManualOn, Events.vector):
                                on = 1;
                                break;
                            case (States.InControlPeriod, Events.vector):
                                on = _latestVector.Reached(controlperiodtimeoff) ? 0 : 1;
                                break;
                            case (States.InControlPeriod, Events.oven):
                                var secondsOn = Math.Min(
                                    (Math.Min(target, _config.MaxTemperature) - ovensensor) * _config.ProportionalGain,
                                    _config.MaxOutputPercentage * _config.ControlPeriod.TotalSeconds
                                    );
                                controlperiodtimeoff = nextcontrolperiod.ToVectorDate().AddMilliseconds(-_config.ControlPeriod.TotalMilliseconds)
                                    .AddMilliseconds(secondsOn < 0.1d ? 0 : (int)(secondsOn * 1000)).ToVectorDouble();
                                on = _latestVector.Reached(controlperiodtimeoff) ? 0 : 1;
                                break;
                            default: //any state without event actions goes here
                                break;
                        }
                    }

                    if (oldState != State)
                        MakeDecision(Events.none); //ensure completion transitions run
                }
            }

            public class Indexes
            {
                public int state { get; } = -1;
                public int target { get; internal set; } = -1;
                public int on { get; internal set; } = -1;
                public int nextcontrolperiod { get; } = -1;
                public int controlperiodtimeoff { get; } = -1;
                public int ovensensor { get; internal set; } = -1;
                public int[] TemperatureBoardsStates;

                public Indexes(CA.LoopControlPluginBase.VectorDescription desc, Config _config)
                {
                    TemperatureBoardsStates = new int[_config.TemperatureBoardStateSensorNames.Count];
                    Array.Fill(TemperatureBoardsStates, -1);

                    for (int i = 0; i < desc.Count; i++)
                    {
                        var field = desc[i];
                        if (field == $"{_config.Name}_state")
                            state = i;
                        if (field == _config.AreaName)
                            target = i;
                        if (field == $"{_config.Name}_onoff")
                            on = i;
                        if (field == $"{_config.Name}_nextcontrolperiod")
                            nextcontrolperiod = i;
                        if (field == $"{_config.Name}_controlperiodtimeoff")
                            controlperiodtimeoff = i;
                        if (field == _config.OvenSensor)
                            ovensensor = i;
                        for (int j = 0; j < TemperatureBoardsStates.Length; j++)
                            if (field == _config.TemperatureBoardStateSensorNames[j])
                                TemperatureBoardsStates[j] = i;
                    }

                    if (state == -1) throw new ArgumentException($"Field used by '{_config.Name}' is not in the vector description: {_config.Name}_state", nameof(desc));
                    if (target == -1 && _config.Area != -1) throw new ArgumentException($"Field used by '{_config.Name}' is not in the vector description: {_config.AreaName}", nameof(desc));
                    if (on == -1) throw new ArgumentException($"Field used by '{_config.Name}' is not in the vector description: {_config.Name}_onoff", nameof(desc));
                    if (nextcontrolperiod == -1) throw new ArgumentException($"Field used by '{_config.Name}' is not in the vector description: {_config.Name}_nextcontrolperiod", nameof(desc));
                    if (controlperiodtimeoff == -1) throw new ArgumentException($"Field used by '{_config.Name}' is not in the vector description: {_config.Name}_controlperiodtimeoff", nameof(desc));
                    if (_config.OvenSensor != null && ovensensor == -1) throw new ArgumentException($"Field used by '{_config.Name}' is not in the vector description: {_config.OvenSensor}", nameof(desc));
                    var missingIndex = Array.IndexOf(TemperatureBoardsStates, -1);
                    if (missingIndex >= 0) throw new ArgumentException($"Field used by '{_config.Name}' is not in the vector description: {_config.TemperatureBoardStateSensorNames[missingIndex]}", nameof(desc));
                }
            }
#pragma warning restore IDE1006 // Naming Styles
        }

        public class OvenAreaDecision : LoopControlDecision
        {
            public enum States { initial, Off, Running }
            public enum Events { none, vector, oven, ovenarea, emergencyshutdown };
            private readonly (string prefix, Events e, bool targetRequired)[] _eventsMap;
            private readonly Config _config;

            private Indexes? _indexes;
            public override string Name => _config.Name;
            public override PluginField[] PluginFields => new PluginField[] { $"{Name}_state", $"{Name}_target" };
            public override string[] HandledEvents => new[] { "oven", "emergencyshutdown" };
            public OvenAreaDecision(Config config)
            {
                _config = config;
                _eventsMap = new (string prefix, Events e, bool targetRequired)[] {
                    (prefix: "oven", Events.oven, true),
                    (prefix: $"ovenarea {config.Area}", Events.ovenarea, true),
                    (prefix: $"ovenarea all", Events.ovenarea, true),
                    (prefix: "emergencyshutdown", Events.emergencyshutdown, false)};

            }

            public override void Initialize(CA.LoopControlPluginBase.VectorDescription desc) => _indexes = new(desc, _config);
            public override void MakeDecision(CA.LoopControlPluginBase.DataVector vector, List<string> events)
            {
                if (_indexes == null) throw new InvalidOperationException("Unexpected call to MakeDecision before Initialize was called first");
                var model = new Model(vector, _indexes);

                foreach (var e in events)
                foreach (var (prefix, typedEvent, targetRequired) in _eventsMap)
                {
                    if (!e.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) 
                        continue;
                    var valueSpan = e.AsSpan()[prefix.Length..].Trim();
                    if (!valueSpan.TryToDouble(out var data) && targetRequired)
                        continue; //CommandHandler.AddDecisions already causes the command to be reported as rejected on failures to parse, so here we just skip the command.
                    model.MakeDecision(typedEvent, data);
                }

                model.MakeDecision(Events.vector);
            }

            public class Config
            {
                public Config(string name, int area)
                {
                    Name = name;
                    Area = area;
                }

                public int Area { get; init; }
                public string Name { get; }
            }

#pragma warning disable IDE1006 // Naming Styles - decisions are coded using a similar approach to decisions plugins, which avoid casing rules in properties to more have naming more similar to the original fields
            public ref struct Model
            {
                private readonly Indexes _indexes;
                private readonly CA.LoopControlPluginBase.DataVector _latestVector;

                public Model(CA.LoopControlPluginBase.DataVector latestVector, Indexes indexes)
                {
                    _latestVector = latestVector;
                    _indexes = indexes;
                }
                public States State { get => (States)_latestVector[_indexes.state]; set => _latestVector[_indexes.state] = (int)value; }
                public double target { get => _latestVector[_indexes.target]; set => _latestVector[_indexes.target] = value; }

                internal void MakeDecision(Events e, double data = 0)
                {
                    var oldState = State;
                    State = (oldState, e) switch
                    {
                        (States.initial, _) => States.Off,
                        (States.Off, Events.vector) when target > 0 => States.Running,
                        (States.Running, Events.emergencyshutdown) => States.Off,
                        (States.Running, Events.vector) when target == 0 => States.Off,
                        (var s, _) => s //no transition by default
                    };
                    if (oldState != State)
                    {
                        switch (State)
                        {
                            case States.Off:
                                target = 0;
                                break;
                            default: //any state without entry actions goes here
                                break;
                        }
                    }
                    if (oldState == State)
                    {
                        switch ((State, e))
                        {
                            case (States.Off or States.Running, Events.oven or Events.ovenarea):
                                target = data;
                                break;
                            default: //any state without event actions goes here
                                break;
                        }
                    }

                    if (oldState != State)
                        MakeDecision(Events.none); //ensure completion transitions run
                }
            }

            public class Indexes
            {
                public int state { get; } = -1;
                public int target { get; internal set; } = -1;

                public Indexes(CA.LoopControlPluginBase.VectorDescription desc, Config _config)
                {
                    for (int i = 0; i < desc.Count; i++)
                    {
                        var field = desc[i];
                        if (field == $"{_config.Name}_state")
                            state = i;
                        if (field == $"{_config.Name}_target")
                            target = i;
                    }

                    if (state == -1) throw new ArgumentException($"Field used by '{_config.Name}' is not in the vector description: {_config.Name}_state", nameof(desc));
                    if (target == -1) throw new ArgumentException($"Field used by '{_config.Name}' is not in the vector description: {_config.Name}_target", nameof(desc));
                }
            }
#pragma warning restore IDE1006 // Naming Styles
        }
    }
}
