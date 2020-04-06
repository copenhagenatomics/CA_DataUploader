using System;
using System.Collections.Generic;
using System.Linq;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfTank : IOconfDriver
    {
        public IOconfTank(string row, int lineNum) : base(row, lineNum, "Tank")
        {
            var list = ToList();
            if (!int.TryParse(list[1], out TankNumber)) throw new Exception("IOconfTank: wrong tank number: " + row);
            Valve = IOconfFile.GetValve().Single(x => x.Name == list[2]);
            Pressure = IOconfFile.GetPressure().Single(x => x.Name == list[3]);
            if (!Enum.TryParse<FlowDirection>(list[4], out flowDirection)) throw new Exception("IOconfTank: in/out not defined correctly :" + row);
            if (!Enum.TryParse<SafeValue>(list[5], out safeValue)) throw new Exception("IOconfTank: safe value not defined correctly :" + row);
        }

        public int TankNumber;
        public IOconfValve Valve;
        public IOConfPressure Pressure;
        public FlowDirection flowDirection;
        public SafeValue safeValue;
    }
}
