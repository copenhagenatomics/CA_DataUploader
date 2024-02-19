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
        Error = 2,      // Below + errors
        Warning = 3,    // Below + warnings
        Normal = 4,     // Below + default output
        Debug = 5       // All possible logging is output. 
    }
}
