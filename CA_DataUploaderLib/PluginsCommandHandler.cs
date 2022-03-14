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
        private readonly List<Action> removeCommandActions = new();
        private readonly List<EventHandler<NewVectorReceivedArgs>> subscribedNewVectorReceivedEvents = new();

        public PluginsCommandHandler(CommandHandler cmd) 
        {
            this.cmd = cmd;
        }

        public event EventHandler<NewVectorReceivedArgs> NewVectorReceived 
        { 
            add { cmd.NewVectorReceived += value; lock(subscribedNewVectorReceivedEvents) subscribedNewVectorReceivedEvents.Add(value); } 
            remove { cmd.NewVectorReceived -= value; lock(subscribedNewVectorReceivedEvents) subscribedNewVectorReceivedEvents.Remove(value); }
        }
        public void AddCommand(string name, Func<List<string>, bool> func) => removeCommandActions.Add(cmd.AddCommand(name, func));
        public void Execute(string command, bool addToCommandHistory) => cmd.Execute(command, addToCommandHistory);
        public async Task<NewVectorReceivedArgs> When(Predicate<NewVectorReceivedArgs> condition, CancellationToken token)
        {
            var tcs = new TaskCompletionSource<NewVectorReceivedArgs>();
            NewVectorReceived += OnNewValue;
            try
            {
                using (CancellationTokenRegistration cancellationTokenRegistration = token.Register(OnCancelled))
                    return await tcs.Task;                
            }
            finally
            {
                NewVectorReceived -= OnNewValue;
            }

            void OnCancelled() => tcs.TrySetCanceled(token);
            void OnNewValue(object sender, NewVectorReceivedArgs e)
            {
                if (condition(e)) tcs.TrySetResult(e);
            }
        }

        public void Dispose()
        {// class is sealed without unmanaged resources, no need for the full disposable pattern.
            lock(subscribedNewVectorReceivedEvents)  
                foreach (var subscribedEvent in subscribedNewVectorReceivedEvents.ToArray())
                    NewVectorReceived -= subscribedEvent;
            foreach (var removeAction in removeCommandActions)
                removeAction();
        }
    }
}