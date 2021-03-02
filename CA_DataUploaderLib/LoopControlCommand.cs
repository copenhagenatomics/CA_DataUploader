﻿using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CA_DataUploaderLib
{
    public abstract class LoopControlCommand : IDisposable
    {
        public abstract string Name { get; }
        public abstract string Description { get; }
        public virtual bool IsHiddenCommand => false;
        public virtual string ArgsHelp => string.Empty;
        private bool disposedValue;
        private CommandHandler cmd;
        private readonly List<Action> removeCommandActions = new List<Action>();
        private readonly List<EventHandler<NewVectorReceivedArgs>> subscribedNewVectorReceivedEvents = 
            new List<EventHandler<NewVectorReceivedArgs>>();

        public void Initialize(CommandHandler cmd) {
            SubscribeToNewVectorReceived(cmd, OnNewVectorReceived);
            this.cmd = cmd;
            AddCommand(Name, Execute);
            if (!IsHiddenCommand)
                AddCommand("help", HelpMenu);
        }
        protected abstract Task Command(List<string> args);
        protected virtual Task OnCommandFailed() { return Task.CompletedTask; }

        protected void AddCommand(string name, Func<List<string>, bool> func) => 
            removeCommandActions.Add(cmd.AddCommand(name, func));

        public void ExecuteCommand(string command) => cmd.Execute(command, false);
        public virtual void OnNewVectorReceived(object sender, NewVectorReceivedArgs e) { }
        public async Task<double> WhenSensorValue(string sensorName, Predicate<double> condition, TimeSpan timeout) => (await When(e => condition(e[sensorName].Value), timeout))[sensorName].Value;
        public async Task<double> WhenSensorValue(string sensorName, Predicate<double> condition, CancellationToken token) => (await When(e => condition(e[sensorName].Value), token))[sensorName].Value;
        public async Task<NewVectorReceivedArgs> When(Predicate<NewVectorReceivedArgs> condition, TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);
            return await When(condition, cts.Token);
        }

        public Task<NewVectorReceivedArgs> When(Predicate<NewVectorReceivedArgs> condition, CancellationToken token)
        { 
            var tcs = new TaskCompletionSource<NewVectorReceivedArgs>();
            SubscribeToNewVectorReceived(cmd, OnNewValue);
            void OnNewValue(object sender, NewVectorReceivedArgs e)
            {
                if (condition(e))
                    tcs.TrySetResult(e);
                else if (token.IsCancellationRequested)
                    tcs.TrySetCanceled(token);
                else // still waiting for condition to be met, do not unsubscribe yet
                    return; 

                UnSubscribeToNewVectorReceived(cmd, OnNewValue);
            }

            return tcs.Task;
        }
        public TimeSpan Milliseconds(double seconds) => TimeSpan.FromMilliseconds(seconds);
        public TimeSpan Seconds(double seconds) => TimeSpan.FromSeconds(seconds);
        public TimeSpan Minutes(double minutes) => TimeSpan.FromMinutes(minutes);
        private void SubscribeToNewVectorReceived(CommandHandler cmd, EventHandler<NewVectorReceivedArgs> handler)
        {
            cmd.NewVectorReceived += handler;
            subscribedNewVectorReceivedEvents.Add(handler);
        }
        private void UnSubscribeToNewVectorReceived(CommandHandler cmd, EventHandler<NewVectorReceivedArgs> handler)
        {
            cmd.NewVectorReceived -= handler;
            subscribedNewVectorReceivedEvents.Remove(handler);
        }
        private bool HelpMenu(List<string> _)
        {
            CALog.LogInfoAndConsoleLn(LogID.A, $"{Name + ArgsHelp,-26}- {Description}");
            return true;
        }

        private bool Execute(List<string> args)
        {
            Task.Run(async () =>
            {
                try
                {
                    await Command(args);
                }
                catch (TaskCanceledException)
                {
                    CALog.LogErrorAndConsoleLn(LogID.A, $"{Name} aborted: timed out waiting for a sensor to reach target range");
                    await OnCommandFailed();
                }
                catch (Exception ex)
                {
                    CALog.LogException(LogID.A, ex);
                    await OnCommandFailed();
                }
            });
            return true;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                foreach (var subscribedEvent in subscribedNewVectorReceivedEvents.ToArray())
                    UnSubscribeToNewVectorReceived(cmd, subscribedEvent);
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
