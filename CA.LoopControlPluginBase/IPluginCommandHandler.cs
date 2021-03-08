using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CA.LoopControlPluginBase
{
    /// <remarks>Implementations of IPluginCommandHandler.Dispose are expected to clear the NewVectorReceived subscriptions and added commands</remarks>
    public interface IPluginCommandHandler : IDisposable
    {
        event EventHandler<NewVectorReceivedArgs> NewVectorReceived;
        /// <returns>an action that removes the added command</returns>
        void AddCommand(string name, Func<List<string>, bool> func);
        void Execute(string command, bool addToCommandHistory);
        Task<NewVectorReceivedArgs> When(Predicate<NewVectorReceivedArgs> condition, CancellationToken token);
    }
}