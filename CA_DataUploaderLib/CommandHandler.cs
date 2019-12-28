using CA_DataUploaderLib.IOconf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;

namespace CA_DataUploaderLib
{
    public class CommandHandler : IDisposable
    {

        private bool _running = true;
        private StringBuilder inputCommand = new StringBuilder();
        private Dictionary<string, List<Func<List<string>, bool>>> _commands = new Dictionary<string, List<Func<List<string>, bool>>>();
        private CALogLevel _logLevel = IOconfFile.GetOutputLevel();
        private VectorDescription _vectorDescription;
        private List<double> _dataVector;

        public bool IsRunning { get { return _running; } }

        public CommandHandler()
        {
            new Thread(() => this.LoopForever()).Start();
            AddCommand("escape", Stop);
            AddCommand("help", HelpMenu);
            AddCommand("Run", Run);
        }

        public void AddCommand(string name, Func<List<string>, bool> func)
        {
            if (_commands.ContainsKey(name))
                _commands[name].Add(func);
            else
                _commands.Add(name.ToLower(), new List<Func<List<string>, bool>>{func});
        }

        public void Execute(string command)
        {
            var cmd = command.Split(' ').Select(x => x.Trim()).ToList();
            HandleCommand(cmd);
        }

        public bool AssertArgs(List<string> args, int minimumLen)
        {
            if (args.Count() < minimumLen)
            {
                CALog.LogInfoAndConsoleLn(LogID.A, "Too few arguments for this command");
                Execute("help");
                return false;
            }

            return true;
        }

        public void SetVectorDescription(VectorDescription vectorDescription)
        {
            _vectorDescription = vectorDescription;
        }

        public void NewData(List<double> vector)
        {
            _dataVector = vector;
        }

        public double GetVectorValue(string name)
        {
            var index = _vectorDescription._items.IndexOf(_vectorDescription._items.Single(x => x.Descriptor == name));
            return _dataVector[index];
        }

        private bool Stop(List<string> args)
        {
            _running = false;
            return true;
        }

        private bool Run(List<string> args)
        {
            var asm = Assembly.LoadFrom(args[1]);
            if (asm == null) throw new ArgumentException("Argument 1 (dll) was not found: " + args[1]);

            Type t = asm.GetType(args[2]);
            if (t == null) throw new ArgumentException("Argument 2 (class) was not found: " + args[2]);

            var methodInfo = t.GetMethod(args[3]);
            if (methodInfo == null) throw new ArgumentException("Argument 3 (method) was not found: " + args[3]);

            var o = Activator.CreateInstance(t, this);
            new Thread(() => methodInfo.Invoke(o, null)).Start();

            return true;
        }

        private void LoopForever()
        {
            DateTime start = DateTime.Now;
            while (_running)
            {
                try
                {
                    var cmd = GetCommand();
                    HandleCommand(cmd);
                }
                catch (Exception ex)
                {
                    CALog.LogErrorAndConsole(LogID.A, ex.ToString());
                }
            }

            CALog.LogInfoAndConsoleLn(LogID.A, "Exiting CommandHandler.LoopForever() " + DateTime.Now.Subtract(start).TotalSeconds.ToString() + " seconds");
        }

        private void HandleCommand(List<string> cmd)
        {
            if (cmd == null)  // no NewLine
            {
                // echo to console here
                return;
            }

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
                            if(_logLevel == CALogLevel.Debug)
                                CALog.LogInfoAndConsoleLn(LogID.A, $"Command: {string.Join(" ", cmd)} - command accepted");
                        }
                        else
                        {
                            if(_logLevel == CALogLevel.Debug)
                                CALog.LogInfoAndConsoleLn(LogID.A, $"Command: {string.Join(" ", cmd)} - bad command");
                        }
                    }
                    catch (ArgumentException ex)
                    {
                        CALog.LogInfoAndConsoleLn(LogID.A, ex.Message);
                    }
                }

                if (cmd.First().ToLower() == "help")
                    CALog.LogInfoAndConsoleLn(LogID.A, "-------------------------------------");  // end help menu divider
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
            CALog.LogInfoAndConsoleLn(LogID.A, "-------------------------------------");
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
