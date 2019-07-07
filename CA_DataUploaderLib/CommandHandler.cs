using System;
using System.Collections.Generic;
using System.Linq;

namespace CA_DataUploaderLib
{
    public class CommandHandler
    {
        protected string inputCommand = string.Empty;
        // protected CommandParser _cmdParser = new CommandParser();

        public string InputCommand { get { return inputCommand; } }

        public List<string> GetCommand()
        {
            while (Console.KeyAvailable)
                inputCommand += (char)Console.Read();

            if (inputCommand.Length > 0 && inputCommand[inputCommand.Length - 1] == (char)ConsoleKey.Escape)
                return new List<string> { "Escape" };

            if (!inputCommand.Contains(Environment.NewLine))
                return null;

            return inputCommand.Split(' ').Select(x => x.Trim()).ToList();
        }
    }
}
