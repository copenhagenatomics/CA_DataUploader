using CA_DataUploaderLib.IOconf;
using Humanizer;
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
        private readonly SerialNumberMapper _mapper;
        private DateTime _start = DateTime.Now;
        private readonly StringBuilder inputCommand = new StringBuilder();
        private readonly Dictionary<string, List<Func<List<string>, bool>>> _commands = new Dictionary<string, List<Func<List<string>, bool>>>();
        private readonly CALogLevel _logLevel = IOconfFile.GetOutputLevel();
        private readonly List<string> AcceptedCommands = new List<string>();
        private int AcceptedCommandsIndex = -1;

        public event EventHandler<NewVectorReceivedArgs> NewVectorReceived;
        public bool IsRunning { get { return _running; } }

        public CommandHandler(SerialNumberMapper mapper = null)
        {
            _mapper = mapper;
            new Thread(() => this.LoopForever()).Start();
            AddCommand("escape", Stop);
            AddCommand("help", HelpMenu);
            AddCommand("up", Uptime);
            AddCommand("version", GetVersion);
        }

        /// <returns>an <see cref="Action"/> that can be used to unregister the command.</returns>
        public Action AddCommand(string name, Func<List<string>, bool> func)
        {
            name = name.ToLower();
            if (_commands.ContainsKey(name))
                _commands[name].Add(func);
            else
                _commands.Add(name, new List<Func<List<string>, bool>>{func});

            return () => 
            {
                _commands[name].Remove(func);
                if (_commands[name].Count == 0) 
                    _commands.Remove(name);
            };
        }

        public void Execute(string command, bool addToCommandHistory = true) => HandleCommand(command, addToCommandHistory);

        public bool AssertArgs(List<string> args, int minimumLen)
        {
            if (args.Count() < minimumLen)
            {
                CALog.LogInfoAndConsoleLn(LogID.A, "Too few arguments for this command");
                Execute("help", false);
                return false;
            }

            return true;
        }

        public void OnNewVectorReceived(List<SensorSample> vector)
        {
            NewVectorReceived?.Invoke(this, new NewVectorReceivedArgs(vector));
        }
        
        private bool Stop(List<string> args)
        {
            _running = false;
            return true;
        }

        private void LoopForever()
        {
            _start = DateTime.Now;
            while (_running)
            {
                try
                {
                    var cmd = GetCommand();
                    HandleCommand(cmd, true);
                }
                catch (Exception ex)
                {
                    CALog.LogErrorAndConsoleLn(LogID.A, ex.ToString());
                }
            }

            CALog.LogInfoAndConsoleLn(LogID.A, "Exiting CommandHandler.LoopForever() " + DateTime.Now.Subtract(_start).Humanize(5));
        }

        private void HandleCommand(string cmdString, bool addToCommandHistory)
        {
            if (cmdString == null)  // no NewLine
            {
                // echo to console here
                return;
            }

            var cmd = cmdString.Split(' ').Select(x => x.Trim()).ToList();

            CALog.LogInfoAndConsoleLn(LogID.A, ""); // this ensures that next command start on a new line. 
            if (!cmd.Any())
            {
                if(_logLevel == CALogLevel.Debug)
                    CALog.LogInfoAndConsoleLn(LogID.A, $"Command: {inputCommand.Replace(Environment.NewLine, "")} - bad command");

                inputCommand.Clear();
                return;
            }

            inputCommand.Clear();
            if (_commands.ContainsKey(cmd.First().ToLower()))
            {
                foreach (var func in _commands[cmd.First().ToLower()])
                {
                    try
                    {
                        if (func.Invoke(cmd))
                        {
                            if (cmdString != "help")
                                CALog.LogInfoAndConsoleLn(LogID.A, $"Command: {cmdString} - command accepted");
                            OnCommandAccepted(cmdString, addToCommandHistory);
                        }
                        else
                            CALog.LogInfoAndConsoleLn(LogID.A, $"Command: {cmdString} - bad command");
                    }
                    catch (ArgumentException ex)
                    {
                        CALog.LogInfoAndConsoleLn(LogID.A, $"Command: {cmdString} - invalid arguments", ex);
                    }
                }

                if (cmd.First().ToLower() == "help")
                    CALog.LogInfoAndConsoleLn(LogID.A, "-------------------------------------");  // end help menu divider
            }
            else
            {
                CALog.LogInfoAndConsoleLn(LogID.A, $"Command: {cmdString} - unknown command");
            }
        }

        private void OnCommandAccepted(string cmdString, bool addToCommandHistory)
        {
            if (!addToCommandHistory || AcceptedCommands.LastOrDefault() == cmdString)
                return;

            AcceptedCommands.Add(cmdString);
            AcceptedCommandsIndex = AcceptedCommands.Count;
        }

        private string GetCommand()
        {
            var info = Console.ReadKey(true);
            while (info.Key != ConsoleKey.Enter && info.Key != ConsoleKey.Escape && _running)
            {
                if (info.Key == ConsoleKey.Backspace)
                {
                    Console.Write("\b \b"); // https://stackoverflow.com/a/53976873/66372
                    if (inputCommand.Length > 0)
                        inputCommand.Remove(inputCommand.Length - 1, 1);
                }
                else if (info.Key == ConsoleKey.UpArrow)
                    RestorePreviousAcceptedCommand();
                else if (info.Key == ConsoleKey.DownArrow)
                    RestoreNextAcceptedCommand();
                else
                {
                    Console.Write(info.KeyChar);
                    inputCommand.Append(info.KeyChar);
                }
                info = Console.ReadKey(true);
            }

            if (info.Key == ConsoleKey.Escape)
            {
                return "escape";
            }

            if (info.Key != ConsoleKey.Enter)
                return null;

            return inputCommand.ToString();
        }

        public static int GetCmdParam(List<string> cmd, int index, int defaultValue) => 
            cmd.Count() > index && int.TryParse(cmd[index], out int value) ? value : defaultValue;

        private void RestoreNextAcceptedCommand()
        {
            if (AcceptedCommandsIndex >= AcceptedCommands.Count - 1)
                return;

            AcceptedCommandsIndex++;
            ReplaceInputCommand(AcceptedCommands[AcceptedCommandsIndex]);
        }

        private void RestorePreviousAcceptedCommand()
        {
            if (AcceptedCommandsIndex > 0)

            AcceptedCommandsIndex--;
            ReplaceInputCommand(AcceptedCommands[AcceptedCommandsIndex]);
        }

        private void ReplaceInputCommand(string cmd)
        {
            inputCommand.Clear();
            inputCommand.Append(cmd);
            Console.Write("\r" + new string(' ', Console.WindowWidth) + "\r"); // clear line first https://stackoverflow.com/a/14083947/66372
            Console.Write("\r" + cmd);
        }

        private bool HelpMenu(List<string> args)
        {
            CALog.LogInfoAndConsoleLn(LogID.A, "-------------------------------------");
            CALog.LogInfoAndConsoleLn(LogID.A, "");
            CALog.LogInfoAndConsoleLn(LogID.A, "Commands: ");
            CALog.LogInfoAndConsoleLn(LogID.A, "Esc                       - press Esc key to shut down");
            CALog.LogInfoAndConsoleLn(LogID.A, "help                      - print the full list of available commands");
            CALog.LogInfoAndConsoleLn(LogID.A, "up                        - print how long the service has been running");
            CALog.LogInfoAndConsoleLn(LogID.A, "version                   - print the software version and hardware of this system");
            return true;
        }

        private bool Uptime(List<string> args)
        {
            CALog.LogInfoAndConsoleLn(LogID.A, DateTime.Now.Subtract(_start).Humanize(5));
            return true;
        }

        private bool GetVersion(List<string> args)
        {
            CALog.LogInfoAndConsoleLn(LogID.A, RpiVersion.GetSoftware() 
                                            + Environment.NewLine 
                                            + RpiVersion.GetHardware()
                                            + string.Join(Environment.NewLine, _mapper.McuBoards.Select(x => x.ToString())));
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
