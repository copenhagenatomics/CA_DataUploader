using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CA_DataUploaderLib
{
    public class LoopControlPlugin : IDisposable
    {
        private bool disposedValue;
        private readonly CommandHandler cmd;
        private readonly List<Action> removeCommandActions = new List<Action>();
        private readonly List<EventHandler<NewVectorReceivedArgs>> subscribedNewVectorReceivedEvents = 
            new List<EventHandler<NewVectorReceivedArgs>>();

        public LoopControlPlugin(CommandHandler cmd)
        {
            cmd.NewVectorReceived += OnNewVectorReceived;
            subscribedNewVectorReceivedEvents.Add(OnNewVectorReceived);
            this.cmd = cmd;

        }

        protected void AddCommand(string name, Func<List<string>, bool> func) => 
            removeCommandActions.Add(cmd.AddCommand(name, func));

        protected void ExecuteCommand(string command) => cmd.Execute(command);
        protected virtual void OnNewVectorReceived(object sender, NewVectorReceivedArgs e) { }
        protected async Task<double> WhenSensorValue(string sensorName, Predicate<double> condition, TimeSpan timeout) => (await When(e => condition(e[sensorName].Value), timeout))[sensorName].Value;
        protected async Task<double> WhenSensorValue(string sensorName, Predicate<double> condition, CancellationToken token) => (await When(e => condition(e[sensorName].Value), token))[sensorName].Value;
        protected async Task<NewVectorReceivedArgs> When(Predicate<NewVectorReceivedArgs> condition, TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);
            return await When(condition, cts.Token);
        }

        protected Task<NewVectorReceivedArgs> When(Predicate<NewVectorReceivedArgs> condition, CancellationToken token)
        { 
            var tcs = new TaskCompletionSource<NewVectorReceivedArgs>();
            cmd.NewVectorReceived += OnNewValue;
            subscribedNewVectorReceivedEvents.Add(OnNewValue);
            void OnNewValue(object sender, NewVectorReceivedArgs e)
            {
                var matchesCondition = condition(e);
                var isCancelled = token.IsCancellationRequested;
                if (matchesCondition)
                    tcs.TrySetResult(e);
                else if (isCancelled)
                    tcs.TrySetCanceled(token);

                if (matchesCondition || isCancelled)
                {
                    cmd.NewVectorReceived -= OnNewValue;
                    subscribedNewVectorReceivedEvents.Remove(OnNewValue);
                }
            }

            return tcs.Task;
        }
        protected TimeSpan Milliseconds(double seconds) => TimeSpan.FromMilliseconds(seconds);
        protected TimeSpan Seconds(double seconds) => TimeSpan.FromSeconds(seconds);
        protected TimeSpan Minutes(double minutes) => TimeSpan.FromMinutes(minutes);

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                foreach (var subscribedEvent in subscribedNewVectorReceivedEvents)
                    cmd.NewVectorReceived -= subscribedEvent;
                foreach (var removeAction in removeCommandActions)
                    removeAction();
                disposedValue = true;
            }
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
