using System;

namespace CA_DataUploaderLib
{
    [Serializable]
    public enum DataTypeEnum
    {
        Unknown = 0,
        Input = 1,
        State = 2,
        Output = 3
    }

    public enum CALogLevel     // see also Syste.LogLevel
    {
        None = 0,       // Silent
        Exception = 1,  // Exceptions
        Error = 2,      // Above + errors
        Warning = 3,    // Above+ warnings
        Normal = 4,     // Above + default output
        Debug = 5       // All possible logging is output. 
    }
}
