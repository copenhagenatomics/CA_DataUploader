namespace CA_DataUploaderLib
{
    public class AlertFiredArgs
    {
        public AlertFiredArgs(string message)
        {
            Message = message;
        }

        public string Message { get; }
    }
}