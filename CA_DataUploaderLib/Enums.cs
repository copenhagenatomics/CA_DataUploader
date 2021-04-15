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
        None = 0,   // silent
        Normal = 1,  // Default output
        Exception = 2,  // only exceptions
        Error = 3,  // only exceptions and errors
        Warning = 4, // all of the above + warnings
        Debug = 5   // all possible loggint is output. 
    }
}
