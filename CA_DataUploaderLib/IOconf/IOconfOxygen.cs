﻿using System;
using System.Collections.Generic;
using System.Linq;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfOxygen : IOconfInput
    {
        public IOconfOxygen(string row) : base(row, "Oxygen")
        {
            var list = ToList();
            Name = list[1];
            BoxName = list[2];
            SetMap(BoxName);
        }
        
    }
}