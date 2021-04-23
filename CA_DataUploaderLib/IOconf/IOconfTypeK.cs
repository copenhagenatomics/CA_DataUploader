using System;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfTypeK : IOconfInput
    {
        public bool AllJunction { get; private set; }
        public IOconfTypeK(string row, int lineNum) : base(row, lineNum, "TypeK", false, true, null)
        {
            format = "TypeK;Name;BoxName;[port number];[skip/all]";

            var list = ToList();
            AllJunction = false;
            if (list[3].ToLower() == "all")
            {
                AllJunction = true;   // all => special command to show all junction temperatures including the first as average (used for calibration)
                PortNumber = 0;
            }
            else if (!Skip && HasPort)
                throw new Exception("IOconfTypeK: wrong port number: " + row);
            else if (!Skip && (PortNumber < 0 || PortNumber > 33)) 
                throw new Exception("IOconfTypeK: invalid port number: " + row);
        }
    }
}
