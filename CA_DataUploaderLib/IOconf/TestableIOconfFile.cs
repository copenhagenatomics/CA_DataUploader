using System;
using System.Collections.Generic;

namespace CA_DataUploaderLib.IOconf
{
    public class TestableIOconfFile
    {
        private static TestableIOconfFile? instance;

        public static TestableIOconfFile Instance 
        { 
            get => instance ?? new() { GetMap = IOconfFile.GetMap, GetLoopName = IOconfFile.GetLoopName }; 
            set => instance = value; 
        }
        public Func<IEnumerable<IOconfMap>> GetMap { get; init; } = () => throw new NotImplementedException("missing test override for GetMap");
        public Func<string> GetLoopName { get; init; } = () => "TestableIOconfFileloop";

        public static IDisposable Override(TestableIOconfFile newInstance)
        {
            var revertOnDispose = new RevertToOldInstanceOnDispose(instance);
            instance = newInstance;
            return revertOnDispose;
        }

        private sealed class RevertToOldInstanceOnDispose : IDisposable
        {
            private readonly TestableIOconfFile? oldInstance;
            public RevertToOldInstanceOnDispose(TestableIOconfFile? oldInstance) => this.oldInstance = oldInstance;
            public void Dispose() => instance = oldInstance;
        }
    }
}
