#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;
using CA_DataUploaderLib.IOconf;

namespace CA_DataUploaderLib
{
    /// <remarks>
    /// Fired alerts are currently included in the next cycle after they triggered (they are reacting to <see cref="CommandHandler.NewVectorReceived"/> which is only available after the decision is made).
    /// 
    /// Any commands triggered by the alert are executed as an user command, which also means they trigger on the next decision cycle (instead of triggering locally outside a decision cycle).
    /// This also means the alert's commands will show in the event log.
    /// 
    /// A host that executes the decisions in a leader node, can use the <see cref="Disabled"/> to avoid the alerts triggering in non leader nodes when <see cref="CommandHandler.NewVectorReceived"/> runs.
    /// On the other hand, a host that cross checks decisions by running them in multiple nodes, needs to consider the alert events and related commands will trigger in all nodes.
    /// </remarks>
    public class Alerts
    {
        public bool Disabled
        {
            get => disabled;
            set
            {
                disabled = value;
                if (!value)
                {
                    foreach (var (alert, _) in _alerts)
                        alert.ResetState();
                }
            }
        }

        private (IOconfAlert alert, int sensorIndex)[] _alerts = [];
        private readonly CommandHandler _cmd;
        private bool disabled;

        public Alerts(IIOconf ioconf, CommandHandler cmd) : base()
        {
            _cmd = cmd;
            cmd.FullVectorDescriptionCreated += DescriptionCreated;

            void DescriptionCreated(object? sender, VectorDescription desc)
            {
                _alerts = [.. GetAlerts(desc, ioconf, _cmd)];
                var reader = _cmd.GetReceivedVectorsReader();
                _ = Task.Run(() => CheckAlertsOnReceivedVectors(reader));
            }
        }

        private async void CheckAlertsOnReceivedVectors(DataVectorReader reader)
        {
            try
            {
                await foreach (var vector in reader.ReadAllAsync(_cmd.StopToken))
                {
                    if (Disabled) continue;

                    var timestamp = vector.Timestamp;
                    foreach (var (alert, sensorIndex) in _alerts)
                    {
                        if (!alert.CheckValue(vector[sensorIndex], timestamp))
                            continue;

                        if (alert.EventType == EventType.Alert)
                            _cmd.FireAlert(alert.Message, timestamp);
                        else
                            CALog.LogErrorAndConsoleLn(LogID.A, alert.Message);

                        if (alert.Command == default)
                            continue;

                        foreach (var commands in alert.Command.Split('|'))
                        {
                            try
                            {
                                _cmd.Execute(commands, true);
                            }
                            catch (Exception ex)
                            {//note that a distributed host might postpone the execution of the command above, but it would then be responsible of logging information about any error.
                                CALog.LogError(LogID.A, $"Unexpected error running command for alert {alert.Name}", ex);
                            }
                        }
                    }
                }
            }
            catch (ChannelClosedException) { }
            catch (OperationCanceledException) { }
        }

        private static List<(IOconfAlert alert, int sensorIndex)> GetAlerts(VectorDescription vectorDesc, IIOconf ioconf, CommandHandler cmd)
        {
            var indexes = vectorDesc._items.Select((f, i) => (f, i)).ToDictionary(f => f.f.Descriptor, f => f.i);
            var alerts = new List<(IOconfAlert alert, int sensorIndex)>();
            var alertsDefinitions = ioconf.GetAlerts()
                .Concat(vectorDesc._items.Where(i => i.Descriptor.EndsWith("_alert")).Select(i => new IOconfAlert($"Alert;{i.Descriptor};{i.Descriptor} = 1;0", 0, EventType.Alert)))
                .Concat(vectorDesc._items.Where(i => i.Descriptor.EndsWith("_error")).Select(i => new IOconfAlert($"Alert;{i.Descriptor};{i.Descriptor} = 1;0", 0, EventType.LogError)));
            foreach (var alert in alertsDefinitions)
            {
                if (!indexes.TryGetValue(alert.Sensor, out var index))
                    throw new FormatException($"Alert: {alert.Name} points to missing vector field: {alert.Sensor}");
                if (alert.Command != default && cmd == null)
                    throw new FormatException($"Alert: {alert.Name} has command configured, but command handler is not available to trigger it");
                alerts.Add((alert, index));
            }

            return alerts;
        }
    }
}