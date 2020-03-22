﻿using System;
using System.Linq;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfOven : IOconfDriver
    {
        public IOconfOven(string row) : base(row, "Oven")
        {
            var list = ToList();
            if (list[1].ToLower() == "max") 
                OvenArea = 0;
            else if (!int.TryParse(list[1], out OvenArea)) 
                throw new Exception("IOconfOven: wrong OvenArea number: " + row);
            
            HeatingElement = IOconfFile.GetHeater().Single(x => x.Name == list[2]);
            TypeK = IOconfFile.GetTypeK().Single(x => x.Name == list[3]);
        }

        public int OvenArea; // oven area 0 = max temperature sensor. 
        public IOconfHeater HeatingElement;
        public IOconfTypeK TypeK;

        public bool OvenAreaMax { get { return OvenArea == 0; } }
    }
}
