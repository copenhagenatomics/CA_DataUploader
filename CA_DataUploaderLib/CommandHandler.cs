using CA_DataUploaderLib.IOconf;
using Humanizer;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Loader;
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
        private readonly Dictionary<string, (AssemblyLoadContext ctx, IEnumerable<LoopControlPlugin> instances)> _runningPlugins = 
            new Dictionary<string, (AssemblyLoadContext ctx, IEnumerable<LoopControlPlugin> instances)>();
        private FileSystemWatcher _pluginChangesWatcher;
        readonly Dictionary<string, object> _postponedPluginChangeLocks = new Dictionary<string, object>();

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
            AddCommand("Load", LoadPlugin);
            AddCommand("Unload", UnloadPlugin);
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

        public void Execute(string command) => HandleCommand(command);

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

        public void OnNewVectorReceived(List<SensorSample> vector)
        {
            NewVectorReceived?.Invoke(this, new NewVectorReceivedArgs(vector));
        }
        
        private bool Stop(List<string> args)
        {
            _running = false;
            return true;
        }

        private bool LoadPlugin(List<string> args)
        {
            var isAuto = args.Count > 1 && args[1] == "auto";
            if (args.Count < 2 || isAuto)
            { // load all
                foreach (var assembly in Directory.GetFiles(".", "*.dll"))
                    LoadPlugin(assembly);

                if (isAuto)
                    TrackPluginChanges("*.dll");
                return true;
            }

            try
            {
                LoadPlugin(args[1]);
                return true;
            }
            catch (Exception ex)
            {
                throw new ArgumentException("Error loading plugin for specified arguments", nameof(args), ex);
            }
        }

        private void TrackPluginChanges(string filepattern)
        {
            //created files is already handled by the changed event, while we ignore manual direct renames of the plugin assemblies files
            _pluginChangesWatcher = new FileSystemWatcher(".", filepattern);
            _pluginChangesWatcher.Deleted += OnAssembliesDeleted;
            _pluginChangesWatcher.Changed += OnAssembliesChanged;
            _pluginChangesWatcher.EnableRaisingEvents = true;
        }

        private void OnAssembliesDeleted(object sender, FileSystemEventArgs e) => UnloadPlugin(e.FullPath);
        /// <remarks>
        /// we wait for a second to (un)load the plugin, and ignore it if a new change comes during that time (because the Changed event is fired multiple times in normal situations).
        /// </remarks>
        private void OnAssembliesChanged(object sender, FileSystemEventArgs e)
        {
            var mylock = new object();
            _postponedPluginChangeLocks[e.FullPath] = mylock;
            var timer = new Timer(DelayedChange, (mylock, path: e.FullPath), 1000, Timeout.Infinite);

            void DelayedChange(object state)
            {
                var (myDelayedLock, path) = ((object, string))state;
                if (!_postponedPluginChangeLocks.TryGetValue(path, out var storedLock) || myDelayedLock != storedLock)
                    return;

                _postponedPluginChangeLocks.Remove(path);
                UnloadPlugin(path);
                LoadPlugin(path);
            }
        }

        private void LoadPlugin(string assemblyPath)
        {
            assemblyPath = Path.GetFullPath(assemblyPath);
            var (context, plugins) = PluginsLoader.Load(assemblyPath, this);
            var initializedPlugins = plugins.ToList(); // iterate the enumerable to create/initialize the instances
            if (initializedPlugins.Count == 0) {
                context.Unload();
                return;
            }

            _runningPlugins[assemblyPath] = (context, initializedPlugins);
            Console.WriteLine($"loaded plugins from {assemblyPath} - {string.Join(",", initializedPlugins.Select(e => e.GetType().Name))}");
        }

        private bool UnloadPlugin(List<string> args)
        {
            if (args.Count < 2)
            { // unload all
                foreach (var assembly in _runningPlugins.Keys.ToList())
                    UnloadPlugin(assembly);
                GC.Collect(); // triggers the unload of the assembly (after DoUnloadExtension we no longer have references to the instances)
                return true;
            }

            UnloadPlugin(args[1]);
            GC.Collect(); // triggers the unload of the assembly (after DoUnloadExtension we no longer have references to the instances)
            return true;
        }

        private bool UnloadPlugin(string assemblyPath)
        {
            assemblyPath = Path.GetFullPath(assemblyPath);
            if (!_runningPlugins.TryGetValue(assemblyPath, out var entry))
            {
                Console.WriteLine("no running extension with the specified assembly was found");
                return false;
            }

            foreach (var instance in entry.instances)
                instance.Dispose();
            _runningPlugins.Remove(assemblyPath);
            entry.ctx.Unload();
            Console.WriteLine($"unloaded plugins from {assemblyPath}");
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
                    HandleCommand(cmd);
                }
                catch (Exception ex)
                {
                    CALog.LogErrorAndConsoleLn(LogID.A, ex.ToString());
                }
            }

            CALog.LogInfoAndConsoleLn(LogID.A, "Exiting CommandHandler.LoopForever() " + DateTime.Now.Subtract(_start).Humanize(5));
        }

        private void HandleCommand(string cmdString)
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
                            CALog.LogInfoAndConsoleLn(LogID.A, $"Command: {cmdString} - command accepted");
                            OnCommandAccepted(cmdString);
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

        private void OnCommandAccepted(string cmdString)
        {
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
