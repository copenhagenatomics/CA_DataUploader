﻿using System;
using System.Collections.Generic;
using System.Linq;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfLight : IOconfOut230Vac
    {
        public IOconfLight(string row, IEnumerable<IOconfMap> map) : base(row, "Light", map) { }
    }
}