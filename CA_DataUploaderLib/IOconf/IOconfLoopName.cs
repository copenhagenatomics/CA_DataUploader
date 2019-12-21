using System;
using System.Collections.Generic;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfLoopName : IOconfState
    {
        public IOconfLoopName(string row) : base(row, "LoopName")
        {
            var list = ToList();
            Name = list[0];
            if(!Enum.TryParse<CALogLevel>(list[1], out LogLevel)) throw new Exception("IOconfLoopName: wrong LogLevel: " + row);
            Name = list[2];
        }

        public string Name;
        public CALogLevel LogLevel;
        public string Server;
    }
}
