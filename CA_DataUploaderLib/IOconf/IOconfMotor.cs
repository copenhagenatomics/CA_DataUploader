using System;
using System.Collections.Generic;
using System.Linq;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfMotor : IOconfOutput
    {
        public string Direction;

        public IOconfMotor(string row, int lineNum) : base(row, lineNum, "Motor")
        {
            var list = ToList();
            Name = list[1];
            BoxName = list[2];
            SetMap(BoxName);
            Direction = list[3];
        }
    }
}
