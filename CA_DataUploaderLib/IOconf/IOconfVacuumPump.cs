using System;
using System.Collections.Generic;
using System.Linq;

namespace CA_DataUploaderLib.IOconf
{
   public class IOconfVacuumPump : IOconfOut230Vac
    {
        public IOconfVacuumPump(string row, int lineNum) : base(row, lineNum, "VacuumPump") {}
    }
}
