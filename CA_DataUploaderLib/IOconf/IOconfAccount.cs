using System.Collections.Generic;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfAccount : IOconfState
    {
        public IOconfAccount(string row) : base(row, "Account")
        {
            var list = ToList();
            Name = list[0];
            Email = list[1];
            Password = list[2];
        }

        public string Name;
        public string Email;
        public string Password;
    }
}
