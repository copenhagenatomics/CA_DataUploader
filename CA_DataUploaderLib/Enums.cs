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

    public enum PowerPhase
    {
        Null = 0,
        Phase1 = 1,
        Phase2 = 2,
        Phase3 = 3,
        All = 4
    }

    public enum PowerType
    {
        Kanthal = 1,
        Calrod = 2,
        SolonoideValve = 3,
        PumpMotor = 4,
        Aux = 5
    }

    public enum LoopStates
    {
        Paused = 0,
        PairSensorsAndActuators = 1,
        LoopThroughOut230V = 2,
        SaltTargetTemperature = 3,
        ManuelPairSensor = 4,
        OnlyRecordSensorValues = 5
    }

    public enum IOTypes
    {
        LoopName = 0,
        Account = 1,
        Map = 2,
        TypeK = 3,
        Out230Vac = 4,
        Pressure = 5,
        AirFlow = 6,
        LiquidFlow = 7, 
        Motor = 8,
        Scale = 9,
        Light = 10,
        Valve = 11, 
        Heater = 12
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
