#nullable enable
using System;

namespace CA_DataUploaderLib.IOconf
{
    public interface IIOconfLoader
    {
        void AddLoader(string rowType, Func<string, int, IOconfRow> loader);
        Func<string, int, IOconfRow>? GetLoader(ReadOnlySpan<char> rowType);
    }
}
