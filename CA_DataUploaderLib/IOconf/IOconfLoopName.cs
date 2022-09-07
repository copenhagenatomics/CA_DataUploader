using System;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfLoopName : IOconfState
    {
        public IOconfLoopName(string row, int lineNum) : base(row, lineNum, "LoopName")
        {
            Format = "LoopName;Name;DebugLevel;[Server]";

            var list = ToList();
            if(!Enum.TryParse<CALogLevel>(list[2], out LogLevel)) throw new Exception("IOconfLoopName: wrong LogLevel: " + row);
            Server = list.Count > 3 ? list[3] : "https://stagingtsserver.copenhagenatomics.com";
        }

        public static IOconfLoopName Default { get; } = 
            new IOconfLoopName($"LoopName;{ Environment.MachineName };Normal;https://stagingtsserver.copenhagenatomics.com", 0);

        public readonly CALogLevel LogLevel;
        public readonly string Server;
    }
}
