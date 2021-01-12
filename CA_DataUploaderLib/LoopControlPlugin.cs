using System;
using System.Collections.Generic;
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
        protected Task<double> WhenSensorValue(string sensorName, Predicate<double> condition)
        { 
            var tcs = new TaskCompletionSource<double>();
            cmd.NewVectorReceived += OnNewValue;
            subscribedNewVectorReceivedEvents.Add(OnNewValue);
            void OnNewValue(object sender, NewVectorReceivedArgs e)
            {
                double value = e[sensorName].Value;
                if (condition(value))
                {
                    tcs.TrySetResult(value);
                    cmd.NewVectorReceived -= OnNewValue;
                    subscribedNewVectorReceivedEvents.Remove(OnNewValue);
                }
            }

            return tcs.Task;
        }

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
