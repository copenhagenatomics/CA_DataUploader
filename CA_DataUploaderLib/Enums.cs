using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
        InTypeK = 1,
        Out230Vac = 2,
        Pressure = 3,
        AirFlow = 4,
        LiquidFlow = 5, 
        MotorSpeed = 6

    }

    public enum LogLevel
    {
        None = 0,   // silent
        Normal = 1,  // Default output
        Exception = 2,  // only exceptions
        Error = 3,  // only exceptions and errors
        Warning = 4, // all of the above + warnings
        Debug = 5   // all possible loggint is output. 
    }
}
