using System;

namespace CA_DataUploaderLib
{
    public class LoopControlExtension : IDisposable
    {
        public LoopControlExtension(CommandHandler cmd)
        {
            cmd.NewVectorReceived += OnNewVectorReceived;
            this.cmd = cmd;
        }

        protected virtual void OnNewVectorReceived(object sender, NewVectorReceivedArgs e) { } 

        private bool disposedValue;
        private readonly CommandHandler cmd;

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (cmd != null)
                    cmd.NewVectorReceived -= OnNewVectorReceived;

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
