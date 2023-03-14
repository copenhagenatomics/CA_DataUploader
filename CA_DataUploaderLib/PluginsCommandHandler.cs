#nullable enable
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

        public PluginsCommandHandler(CommandHandler cmd) 
        {
            this.cmd = cmd;
            cmd.NewVectorReceived += (s, v) => NewVectorReceived?.Invoke(s, v);
        }

        public event EventHandler<NewVectorReceivedArgs>? NewVectorReceived;
        public void AddCommand(string name, Func<List<string>, bool> func) => removeCommandActions.Add(cmd.AddCommand(name, func));
        //the addToCommandHistory should be renamed isUserCommand, but not changing now to avoid a new plugins base version just for an arg name change
        //note addToCommandHistory is always false, as sent by: LoopControlCommand.ExecuteCommand
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
            void OnNewValue(object? sender, NewVectorReceivedArgs e)
            {
                if (condition(e)) tcs.TrySetResult(e);
            }
        }

        public void Dispose()
        {// class is sealed without unmanaged resources, no need for the full disposable pattern.
            NewVectorReceived = null; //removes all subscribers
            foreach (var removeAction in removeCommandActions)
                removeAction();
        }
    }
}