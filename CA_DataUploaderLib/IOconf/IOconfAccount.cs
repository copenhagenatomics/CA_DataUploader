using System.Collections.Generic;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfAccount : IOconfState
    {
        public IOconfAccount(string row) : base(row, "Account")
        {
            var list = ToList();
            Name = list[1];
            Email = list[2];
            Password = list[3];
        }

        public string Name;
        public string Email;
        public string Password;
    }
}
