using System;
using System.Linq;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfOven : IOconfDriver
    {
        public IOconfOven(string row, int lineNum) : base(row, lineNum, "Oven")
        {
            format = "Oven;Area;HeatingElement;TypeK";

            var list = ToList();
            if (!int.TryParse(list[1], out OvenArea)) 
                throw new Exception($"IOconfOven: wrong OvenArea number: {row} {format}");
            if (OvenArea < 1)
                throw new Exception("Oven area must be a number bigger or equal to 1");
            
            HeatingElement = IOconfFile.GetHeater().Single(x => x.Name == list[2]);
            TypeK = IOconfFile.GetTypeK().Single(x => x.Name == list[3]);
        }

        public int OvenArea;
        public IOconfHeater HeatingElement;
        public IOconfTypeK TypeK;
    }
}
