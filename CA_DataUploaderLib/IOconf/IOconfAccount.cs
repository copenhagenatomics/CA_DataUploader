namespace CA_DataUploaderLib.IOconf
{
    public class IOconfAccount : IOconfState
    {
        public IOconfAccount(string row, int lineNum) : base(row, lineNum, "Account")
        {
            format = "Account;Donald Dock;donald@dock.org;MySecretePassword";
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
