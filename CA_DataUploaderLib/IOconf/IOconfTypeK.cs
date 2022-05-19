using System;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfTypeK : IOconfInput
    {
        public bool AllJunction { get; }
        public IOconfTypeK(string row, int lineNum) : base(row, lineNum, "TypeK", false, true, null)
        {
            Format = "TypeK;Name;BoxName;[port number];[skip/all]";

            var list = ToList();
            AllJunction = false;
            if (list[3].ToLower() == "all")
            {
                AllJunction = true;   // all => special command to show all junction temperatures including the first as average (used for calibration)
                PortNumber = 1;
            }
            else if (!Skip && !HasPort)
                throw new Exception("IOconfTypeK: wrong port number: " + row);
            else if (!Skip && (PortNumber < 1 || PortNumber > 34)) 
                throw new Exception("IOconfTypeK: invalid port number: " + row);
        }

        public override bool IsSpecialDisconnectValue(double value) => value >= 10000;
    }
}
