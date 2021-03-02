using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CA_DataUploaderLib.IOconf;

namespace CA_DataUploaderLib
{
    public class Alerts : LoopControlCommand
    {
        public override string Name => "addalert";
        public override string Description => string.Empty;
        public override bool IsHiddenCommand => true;
        private readonly List<IOconfAlert> _alerts;
        private readonly ServerUploader _uploader;

        public Alerts(VectorDescription vectorDescription, CommandHandler cmd, ServerUploader uploader) : base()
        {
            Initialize(cmd);
            AddCommand("removealert", RemoveAlert);
            _uploader = uploader;
            _alerts = GetAlerts(vectorDescription, cmd);
        }

        private static List<IOconfAlert> GetAlerts(VectorDescription vectorDesc, CommandHandler cmd)
        {
            var alerts = IOconfFile.GetAlerts().ToList();
            var alertsWithoutItem = alerts.Where(a => !vectorDesc.HasItem(a.Sensor)).ToList();
            foreach (var alert in alertsWithoutItem)
                CALog.LogErrorAndConsoleLn(LogID.A, $"ERROR in {Directory.GetCurrentDirectory()}\\IO.conf:{Environment.NewLine} Alert: {alert.Name} points to missing sensor: {alert.Sensor}");
            if (alertsWithoutItem.Count > 0)
                throw new InvalidOperationException("Misconfigured alerts detected");
            if (alerts.Any(a => a.TriggersEmergencyShutdown) && cmd == null)
                throw new InvalidOperationException("Alert with emergency shutdown is configured, but command handler is not available to trigger it");
            return alerts;
        }

        private bool RemoveAlert(List<string> args)
        {
            if (args.Count < 2)
                CALog.LogErrorAndConsoleLn(LogID.A, $"Unexpected format for Removing dynamic alert: {string.Join(',', args)}. Format: removealert AlertName");

            lock (_alerts) 
                _alerts.RemoveAll(a => a.Name == args[1]);
            return true;
        }

        protected override Task Command(List<string> args)
        {
            if (args.Count < 3)
                CALog.LogErrorAndConsoleLn(LogID.A, $"Unexpected format for Dynamic alert: {string.Join(',', args)}. Format: addalert AlertName SensorName comparison value");

            var alert = new IOconfAlert(args[1], string.Join(' ', args.Skip(2)));
            lock (_alerts) 
            {
                _alerts.RemoveAll(a => a.Name == args[1]);
                _alerts.Add(alert);
            }

            return Task.CompletedTask;
        }

        protected override void OnNewVectorReceived(object sender, NewVectorReceivedArgs e)
        {
            var timestamp = DateTime.UtcNow.ToString("yyyy.MM.dd HH:mm:ss");
            var (alertsToTrigger, noSensorAlerts) = GetAlertsToTrigger(e); // we gather alerts separately from triggering, to reduce time locking the _alerts list
            
            foreach (var a in alertsToTrigger)
                TriggerAlert(a, timestamp, a.Message);

            foreach (var a in noSensorAlerts)
            {
                _alerts.Remove(a); // avoid missing sensors alert triggerring repeatedly on every vector received
                TriggerAlert(a, timestamp, $"Failed to find sensor {a.Sensor} for alert {a.Name}");
            }
        }

        private (List<IOconfAlert> alertsToTrigger, List<IOconfAlert> noSensorAlerts) GetAlertsToTrigger(NewVectorReceivedArgs e)
        {
            // we only create the lists later to avoid unused lists on every call
            List<IOconfAlert> alertsToTrigger = null;
            List<IOconfAlert> noSensorAlerts = null;
            lock (_alerts)
                foreach (var a in _alerts)
                {
                    var sample = e.TryGetValue(a.Sensor);
                    if (sample == null)
                        EnsureInitialized(ref noSensorAlerts).Add(a);
                    else if (a.CheckValue(sample.Value))
                        EnsureInitialized(ref alertsToTrigger).Add(a);
                }
            
            return (alertsToTrigger, noSensorAlerts);
        }

        private void TriggerAlert(IOconfAlert a, string timestamp, string message)
        {
            message = timestamp + message;
            CALog.LogErrorAndConsoleLn(LogID.A, message);
            if (a.TriggersEmergencyShutdown)
                ExecuteCommand("emergencyshutdown");

            _uploader.SendAlert(message);
        }

        private List<T> EnsureInitialized<T>(ref List<T> list) => list = list ?? new List<T>();
    }
}