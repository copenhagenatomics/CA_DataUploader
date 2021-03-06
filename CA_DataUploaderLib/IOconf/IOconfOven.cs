﻿using System;
using System.Collections.Generic;
using System.Linq;
using CA_DataUploaderLib.Extensions;

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
            TemperatureSensorName = list[3];
            var filter = IOconfFile.GetFilters().SingleOrDefault(x => x.NameInVector == TemperatureSensorName);
            if (filter != null)
            {
                TypeKs = IOconfFile.GetTypeK().Where(x => filter.SourceNames.Contains(x.Name)).ToList();
                if (TypeKs.Count == 0)
                    throw new Exception($"Failed to find source temperature sensors in filter {TemperatureSensorName} for oven");
            }
            else
            {
                TypeKs = IOconfFile.GetTypeK().Where(x => x.Name == TemperatureSensorName).ToList();
                if (TypeKs.Count == 0)
                    throw new Exception($"Failed to find temperature sensor {TemperatureSensorName} for oven");
            }
            BoardStateSensorNames = TypeKs.Select(k => k.BoardStateSensorName).ToList().AsReadOnly();
        }

        public int OvenArea;
        public IOconfHeater HeatingElement;
        public bool IsTemperatureSensorInitialized => TypeKs.All(k => k.IsInitialized());
        public string TemperatureSensorName { get; }
        public IReadOnlyCollection<string> BoardStateSensorNames {get;}
        private readonly List<IOconfTypeK> TypeKs;
    }
}
