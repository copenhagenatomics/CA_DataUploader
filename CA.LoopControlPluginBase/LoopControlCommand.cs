using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CA.LoopControlPluginBase
{
    public abstract class LoopControlCommand : IDisposable
    {
        public abstract string Name { get; }
        public abstract string Description { get; }
        public virtual bool IsHiddenCommand => false;
        public virtual string ArgsHelp => string.Empty;
        protected ISimpleLogger logger;
        private bool disposedValue;
        private IPluginCommandHandler cmd;

        public void Initialize(IPluginCommandHandler cmd, ISimpleLogger logger) 
        {
            this.logger = logger;
            this.cmd = cmd;
            cmd.NewVectorReceived += OnNewVectorReceived;
            cmd.AddCommand(Name, Execute);
            if (!IsHiddenCommand)
                cmd.AddCommand("help", HelpMenu);
        }
        protected abstract Task Command(List<string> args);
        protected virtual Task OnCommandFailed() { return Task.CompletedTask; }
        public void ExecuteCommand(string command) => cmd.Execute(command, false);
        public virtual void OnNewVectorReceived(object sender, NewVectorReceivedArgs e) { }
        public async Task<double> WhenSensorValue(string sensorName, Predicate<double> condition, TimeSpan timeout) => (await When(e => condition(e[sensorName]), timeout))[sensorName];
        public async Task<double> WhenSensorValue(string sensorName, Predicate<double> condition, CancellationToken token) => (await When(e => condition(e[sensorName]), token))[sensorName];
        public async Task<NewVectorReceivedArgs> When(Predicate<NewVectorReceivedArgs> condition, TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);
            return await When(condition, cts.Token);
        }

        public Task<NewVectorReceivedArgs> When(Predicate<NewVectorReceivedArgs> condition, CancellationToken token) => cmd.When(condition, token);
        public TimeSpan Milliseconds(double seconds) => TimeSpan.FromMilliseconds(seconds);
        public TimeSpan Seconds(double seconds) => TimeSpan.FromSeconds(seconds);
        public TimeSpan Minutes(double minutes) => TimeSpan.FromMinutes(minutes);
        private bool HelpMenu(List<string> _)
        {
            logger.LogInfo($"{Name + ArgsHelp,-26}- {Description}");
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
                    logger.LogError($"{Name} aborted: timed out waiting for a sensor to reach target range");
                    await OnCommandFailed();
                }
                catch (Exception ex)
                {
                    logger.LogError(ex);
                    await OnCommandFailed();
                }
            });
            return true;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposedValue) return;
            cmd?.Dispose();
            cmd = null;
            disposedValue = true;
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
