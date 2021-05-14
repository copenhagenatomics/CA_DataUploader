using CA.LoopControlPluginBase;
using CA_DataUploaderLib.Helpers;
using CA_DataUploaderLib.IOconf;
using Humanizer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace CA_DataUploaderLib
{
    public sealed class CommandHandler : IDisposable
    {
        private bool _running = true;
        private readonly SerialNumberMapper _mapper;
        private DateTime _start = DateTime.Now;
        private readonly StringBuilder inputCommand = new StringBuilder();
        private readonly Dictionary<string, List<Func<List<string>, bool>>> _commands = new Dictionary<string, List<Func<List<string>, bool>>>();
        private readonly CALogLevel _logLevel = IOconfFile.GetOutputLevel();
        private readonly List<string> AcceptedCommands = new List<string>();
        private readonly List<ISubsystemWithVectorData> _subsystems = new List<ISubsystemWithVectorData>();
        private int AcceptedCommandsIndex = -1;
        private Lazy<VectorFilterAndMath> _fullsystemFilterAndMath;

        public event EventHandler<NewVectorReceivedArgs> NewVectorReceived;
        public bool IsRunning { get { return _running; } }

        public CommandHandler(SerialNumberMapper mapper = null)
        {
            _mapper = mapper;
            _fullsystemFilterAndMath = new Lazy<VectorFilterAndMath>(GetFullSystemFilterAndMath);
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

        public void AddSubsystem(ISubsystemWithVectorData subsystem) => _subsystems.Add(subsystem);
        public VectorDescription GetFullSystemVectorDescription() => _fullsystemFilterAndMath.Value.VectorDescription;
        public List<SensorSample> GetFullSystemVectorValues() => 
            _fullsystemFilterAndMath.Value.Apply(_subsystems.SelectMany(s => s.GetValues()).ToList());
        private VectorFilterAndMath GetFullSystemFilterAndMath() => 
            new VectorFilterAndMath(
                new VectorDescription(
                    _subsystems.SelectMany(s => s.GetVectorDescriptionItems()).ToList(),
                    RpiVersion.GetHardware(),
                    RpiVersion.GetSoftware()));
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

        public void OnNewVectorReceived(List<SensorSample> vector) =>
            NewVectorReceived?.Invoke(this, new NewVectorReceivedArgs(vector.ToDictionary(v => v.Name, v => v.Value)));
        
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
            string commandName = cmd.First().ToLower();
            if (!_commands.TryGetValue(commandName, out var commandFunctions))
            {
                CALog.LogInfoAndConsoleLn(LogID.A, $"Command: {cmdString} - unknown command");
                return;
            }

            List<bool> executionResults = RunCommandFunctions(cmdString, addToCommandHistory, cmd, commandFunctions);

            if (commandName == "help")
                CALog.LogInfoAndConsoleLn(LogID.A, "-------------------------------------");  // end help menu divider
            else
                LogAndDisplayCommandResults(cmdString, executionResults);
        }

        private List<bool> RunCommandFunctions(string cmdString, bool addToCommandHistory, List<string> cmd, List<Func<List<string>, bool>> commandFunctions)
        {
            List<bool> executionResults = new List<bool>(commandFunctions.Count);
            foreach (var func in commandFunctions)
            {
                try
                {
                    bool accepted;
                    executionResults.Add(accepted = func.Invoke(cmd));
                    if (accepted)
                        OnCommandAccepted(cmdString, addToCommandHistory); // track it in the history if at least one execution accepted the command
                    else
                        break; // avoid running the command on another subsystem when it was already rejected
                }
                catch (ArgumentException ex)
                {
                    executionResults.Add(false);
                    CALog.LogInfoAndConsoleLn(LogID.A, $"Command: {cmdString} - invalid arguments", ex);
                    break; // avoid running the command on another subsystem when it was already rejected
                }
            }

            return executionResults;
        }

        private static void LogAndDisplayCommandResults(string cmdString, List<bool> executionResults)
        {
            if (executionResults.All(r => r))
                CALog.LogInfoAndConsoleLn(LogID.A, $"Command: {cmdString} - command accepted");
            else if (executionResults.All(r => !r))
                CALog.LogInfoAndConsoleLn(LogID.A, $"Command: {cmdString} - bad command");
            else
                CALog.LogInfoAndConsoleLn(LogID.A, $"Command: {cmdString} - bad command / accepted by some subsystems");
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
                CALog.LogInfoAndConsoleLn(LogID.A, "You are about to stop the control program, type y if you want to continue");
                var confirmedStop = Console.ReadKey().KeyChar == 'y';
                CALog.LogInfoAndConsoleLn(LogID.A, confirmedStop ? "Stop sequence initiated" : "Stop sequence aborted");
                return confirmedStop ? "escape" : null;
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

        public void Dispose()
        { // class is sealed without unmanaged resources, no need for the full disposable pattern.
            _running = false;
        }
    }
}
