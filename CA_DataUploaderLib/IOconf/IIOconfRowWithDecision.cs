#nullable enable
using CA.LoopControlPluginBase;

namespace CA_DataUploaderLib.IOconf
{
    public interface IIOconfRowWithDecision
    {
        LoopControlDecision CreateDecision(IIOconf ioconf);
    }
}
