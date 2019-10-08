using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace CA_DataUploaderLib
{
    public class CommandHandler : IDisposable
    {

        private bool _running = true;
        private StringBuilder inputCommand = new StringBuilder();
        private Dictionary<string, List<Func<List<string>, bool>>> _commands = new Dictionary<string, List<Func<List<string>, bool>>>();

        public bool IsRunning { get { return _running; } }

        public CommandHandler()
        {
            new Thread(() => this.LoopForever()).Start();
            AddCommand("escape", Stop);
            AddCommand("help", HelpMenu);
        }

        public void AddCommand(string name, Func<List<string>, bool> func)
        {
            if (_commands.ContainsKey(name))
                _commands[name].Add(func);
            else
                _commands.Add(name.ToLower(), new List<Func<List<string>, bool>>{func});
        }

        private bool Stop(List<string> args)
        {
            _running = false;
            return true;
        }
        
        private void LoopForever()
        {
            DateTime start = DateTime.Now;
            var logLevel = IOconf.GetOutputLevel();
            while (_running)
            {
                try
                {
                    HandleCommand(logLevel == LogLevel.Debug);
                }
                catch (Exception ex)
                {
                    CALog.LogErrorAndConsole(LogID.A, ex.ToString());
                }
            }

            CALog.LogInfoAndConsoleLn(LogID.A, "Exiting CommandHandler.LoopForever() " + DateTime.Now.Subtract(start).TotalSeconds.ToString() + " seconds");
        }

        private void HandleCommand(bool logDebug)
        {
            var cmd = GetCommand();
            if (cmd == null)  // no NewLine
            {
                // echo to console here
                return;
            }

            if (!cmd.Any())
            {
                if(logDebug)
                    CALog.LogInfoAndConsoleLn(LogID.A, $"Command: {inputCommand.Replace(Environment.NewLine, "")} - bad command");

                inputCommand.Clear();
                return;
            }

            inputCommand.Clear();
            if (_commands.ContainsKey(cmd.First().ToLower()))
            {
                foreach (var func in _commands[cmd.First().ToLower()])
                {
                    if (func.Invoke(cmd))
                    {
                        if(logDebug)
                            CALog.LogInfoAndConsoleLn(LogID.A, $"Command: {string.Join(" ", cmd)} - command accepted");
                    }
                    else
                    {
                        if(logDebug)
                            CALog.LogInfoAndConsoleLn(LogID.A, $"Command: {string.Join(" ", cmd)} - bad command");
                    }
                }
            }
        }

        private List<string> GetCommand()
        {
            var info = Console.ReadKey(true);
            while (info.Key != ConsoleKey.Enter && info.Key != ConsoleKey.Escape && _running)
            {
                Console.Write(info.KeyChar);
                inputCommand.Append(info.KeyChar);
                info = Console.ReadKey(true);
            }

            if (info.Key == ConsoleKey.Escape)
            {
                return new List<string> { "escape" };
            }

            if (info.Key != ConsoleKey.Enter)
                return null;

            return inputCommand.ToString().Split(' ').Select(x => x.Trim()).ToList();
        }

        public static int GetCmdParam(List<string> cmd, int index, int defaultValue)
        {
            if (cmd.Count() > index)
            {
                int value;
                if (int.TryParse(cmd[index], out value))
                    return value;
            }

            return defaultValue;
        }

        private bool HelpMenu(List<string> args)
        {
            CALog.LogInfoAndConsoleLn(LogID.A, "");
            CALog.LogInfoAndConsoleLn(LogID.A, "Commands: ");
            CALog.LogInfoAndConsoleLn(LogID.A, "Esc                       - press Esc key to shut down");
            CALog.LogInfoAndConsoleLn(LogID.A, "help                      - print the full list of available commands");
            return true;
        }

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            _running = false;
            if (!disposedValue)
            {
                disposedValue = true;
            }
        }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
        }

        #endregion

    }
}
