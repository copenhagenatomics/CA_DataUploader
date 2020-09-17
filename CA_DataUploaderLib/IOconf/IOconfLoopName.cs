using System;
using System.Collections.Generic;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfLoopName : IOconfState
    {
        public IOconfLoopName(string row, int lineNum) : base(row, lineNum, "LoopName")
        {
            format = "LoopName;Name;DebugLevel;[Server]";

            var list = ToList();
            Name = list[1];
            if(!Enum.TryParse<CALogLevel>(list[2], out LogLevel)) throw new Exception("IOconfLoopName: wrong LogLevel: " + row);
            if(list.Count > 3)
                Server = list[3];
        }

        public string Name;
        public CALogLevel LogLevel;
        public string Server;
    }
}
