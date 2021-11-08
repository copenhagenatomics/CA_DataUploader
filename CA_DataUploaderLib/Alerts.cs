using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CA.LoopControlPluginBase;
using CA_DataUploaderLib.Extensions;
using CA_DataUploaderLib.IOconf;

namespace CA_DataUploaderLib
{
    public class Alerts : LoopControlCommand
    {
        public override string Name => "addalert";
        public override string Description => string.Empty;
        public override bool IsHiddenCommand => true;
        private readonly List<IOconfAlert> _alerts;
        private readonly CommandHandler _cmd;

        public Alerts(VectorDescription vectorDescription, CommandHandler cmd) : base()
        {
            _cmd = cmd;
            var cmdPlugins = new PluginsCommandHandler(cmd);
            Initialize(cmdPlugins, new PluginsLogger("Alerts"));
            cmdPlugins.AddCommand("removealert", RemoveAlert);
            _alerts = GetAlerts(vectorDescription, cmd);
        }

        private List<IOconfAlert> GetAlerts(VectorDescription vectorDesc, CommandHandler cmd)
        {
            var alerts = IOconfFile.GetAlerts().ToList();
            var alertsWithoutItem = alerts.Where(a => !vectorDesc.HasItem(a.Sensor)).ToList();
            foreach (var alert in alertsWithoutItem)
                logger.LogError($"ERROR in {Directory.GetCurrentDirectory()}\\IO.conf:{Environment.NewLine} Alert: {alert.Name} points to missing sensor: {alert.Sensor}");
            if (alertsWithoutItem.Count > 0)
                throw new InvalidOperationException("Misconfigured alerts detected");
            if (alerts.Any(a => a.Command != default) && cmd == null)
                throw new InvalidOperationException("Alert with command is configured, but command handler is not available to trigger it");
            return alerts;
        }

        private bool RemoveAlert(List<string> args)
        {
            if (args.Count < 2)
            {
                logger.LogError($"Unexpected format for Removing dynamic alert: {string.Join(',', args)}. Format: removealert AlertName");
                return true;
            }

            lock (_alerts) 
                _alerts.RemoveAll(a => a.Name == args[1]);
            return true;
        }

        protected override Task Command(List<string> args)
        {
            if (args.Count < 3)
            {
                logger.LogError($"Unexpected format for Dynamic alert: {string.Join(',', args)}. Format: addalert AlertName SensorName comparison value [rateMinutes] [command]");
                return Task.CompletedTask;
            }

            var alert = new IOconfAlert(args[1], string.Join(' ', args.Skip(2)));
            lock (_alerts) 
            {
                _alerts.RemoveAll(a => a.Name == args[1]);
                _alerts.Add(alert);
            }

            return Task.CompletedTask;
        }

        public override void OnNewVectorReceived(object sender, NewVectorReceivedArgs e)
        {
            var timestamp = e.GetVectorTime();
            var (alertsToTrigger, noSensorAlerts) = GetAlertsToTrigger(e); // we gather alerts separately from triggering, to reduce time locking the _alerts list
            
            foreach (var a in alertsToTrigger ?? Enumerable.Empty<IOconfAlert>())
                TriggerAlert(a, timestamp, a.Message);

            foreach (var a in noSensorAlerts)
            {
                _alerts.Remove(a); // avoid missing sensors alert triggerring repeatedly on every vector received
                TriggerAlert(a, timestamp, $"Failed to find sensor {a.Sensor} for alert {a.Name}");
            }
        }

        private (IEnumerable<IOconfAlert> alertsToTrigger, IEnumerable<IOconfAlert> noSensorAlerts) GetAlertsToTrigger(NewVectorReceivedArgs e)
        {
            // we only create the lists later to avoid unused lists on every call
            List<IOconfAlert> alertsToTrigger = null;
            List<IOconfAlert> noSensorAlerts = null;
            lock (_alerts)
                foreach (var a in _alerts)
                {
                    if (!e.TryGetValue(a.Sensor, out var val))
                        EnsureInitialized(ref noSensorAlerts).Add(a);
                    else if (a.CheckValue(val, e.GetVectorTime()))
                        EnsureInitialized(ref alertsToTrigger).Add(a);
                }
            
            return (
                alertsToTrigger ?? Enumerable.Empty<IOconfAlert>(), 
                noSensorAlerts ?? Enumerable.Empty<IOconfAlert>());
        }

        private void TriggerAlert(IOconfAlert a, DateTime timestamp, string message)
        {
            _cmd.FireAlert(message, timestamp);
            if (a.Command != default)
            {
                foreach (var commands in a.Command.Split('|'))
                    ExecuteCommand(commands);
            }
        }

        private static List<T> EnsureInitialized<T>(ref List<T> list) => list = list ?? new List<T>();
    }
}