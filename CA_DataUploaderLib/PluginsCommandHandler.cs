using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CA.LoopControlPluginBase;

namespace CA_DataUploaderLib
{
    public sealed class PluginsCommandHandler : IPluginCommandHandler
    {
        private readonly CommandHandler cmd;
        private readonly List<Action> removeCommandActions = new List<Action>();
        private readonly List<EventHandler<NewVectorReceivedArgs>> subscribedNewVectorReceivedEvents = new List<EventHandler<NewVectorReceivedArgs>>();

        public PluginsCommandHandler(CommandHandler cmd) 
        {
            this.cmd = cmd;
        }

        public event EventHandler<NewVectorReceivedArgs> NewVectorReceived 
        { 
            add { cmd.NewVectorReceived += value; subscribedNewVectorReceivedEvents.Add(value); } 
            remove { cmd.NewVectorReceived -= value; subscribedNewVectorReceivedEvents.Remove(value); }
        }
        public void AddCommand(string name, Func<List<string>, bool> func) => removeCommandActions.Add(cmd.AddCommand(name, func));
        public void Execute(string command, bool addToCommandHistory) => cmd.Execute(command, addToCommandHistory);
        public Task<NewVectorReceivedArgs> When(Predicate<NewVectorReceivedArgs> condition, CancellationToken token)
        {
            var tcs = new TaskCompletionSource<NewVectorReceivedArgs>();
            NewVectorReceived += OnNewValue;
            void OnNewValue(object sender, NewVectorReceivedArgs e)
            {
                if (condition(e))
                    tcs.TrySetResult(e);
                else if (token.IsCancellationRequested)
                    tcs.TrySetCanceled(token);
                else // still waiting for condition to be met, do not unsubscribe yet
                    return; 

                NewVectorReceived -= OnNewValue;
            }

            return tcs.Task;
        }

        public void Dispose()
        {// class is sealed without unmanaged resources, no need for the full disposable pattern.
            foreach (var subscribedEvent in subscribedNewVectorReceivedEvents.ToArray())
                NewVectorReceived -= subscribedEvent;
            foreach (var removeAction in removeCommandActions)
                removeAction();
        }
    }
}