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
            if (list[3].ToLower() == "skip")
            {
                Skip = true;   // BaseSensorBox will skip reading from this box. Data is comming from other sensors through ProcessLine() instead. You must skip all lines from this box in IO.conf, else it will not work.
            }
            else if (list[3].ToLower() == "all")
            {
                AllJunction = true;   // all => special command to show all junction temperatures including the first as average (used for calibration)
                PortNumber = 0;
            }
            else
            {
                if (!int.TryParse(list[3], out PortNumber)) throw new Exception("IOconfTypeK: wrong port number: " + row);
                if (PortNumber < 0 || PortNumber > 33) throw new Exception("IOconfTypeK: invalid port number: " + row);
            }
        }
    }
}
