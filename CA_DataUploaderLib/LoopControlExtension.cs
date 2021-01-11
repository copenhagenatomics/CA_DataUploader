using System;
using System.Collections.Generic;

namespace CA_DataUploaderLib
{
    public class LoopControlExtension : IDisposable
    {
        private bool disposedValue;
        private readonly CommandHandler cmd;
        private readonly List<Action> removeCommandActions = new List<Action>();

        public LoopControlExtension(CommandHandler cmd)
        {
            cmd.NewVectorReceived += OnNewVectorReceived;
            this.cmd = cmd;

        }

        protected void AddCommand(string name, Func<List<string>, bool> func) => 
            removeCommandActions.Add(cmd.AddCommand(name, func));
        protected virtual void OnNewVectorReceived(object sender, NewVectorReceivedArgs e) { } 

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                cmd.NewVectorReceived -= OnNewVectorReceived;
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
