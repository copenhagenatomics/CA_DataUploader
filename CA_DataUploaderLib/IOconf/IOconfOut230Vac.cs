using System;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfOut230Vac : IOconfOutput
    {
        public IOconfOut230Vac(string row, string type) : base(row, type)
        {
            var list = ToList();
            Name = list[1];
            BoxName = list[2];
            SetMap(BoxName); 
            if (!int.TryParse(list[3], out PortNumber)) throw new Exception("Out230VacInfo: wrong port number: " + row);

        }
    }
}
