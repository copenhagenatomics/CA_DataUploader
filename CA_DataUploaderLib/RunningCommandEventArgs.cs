using System.Collections.Generic;
using System.ComponentModel;

namespace CA_DataUploaderLib
{
    public class RunningCommandEventArgs : CancelEventArgs
    {
        public RunningCommandEventArgs(string rawCommand, List<string> parsedCommand)
        {
            RawCommand = rawCommand;
            ParsedCommand = parsedCommand;
        }

        public string RawCommand { get; }
        public List<string> ParsedCommand { get; }
    }
}