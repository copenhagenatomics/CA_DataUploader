using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using CA.LoopControlPluginBase;
using CA_DataUploaderLib.IOconf;

namespace CA_DataUploaderLib
{
    public class PluginsLoader
    { // see https://docs.microsoft.com/en-us/dotnet/core/tutorials/creating-app-with-plugin-support
        private readonly string targetFolder;
        readonly Dictionary<string, (AssemblyLoadContext ctx, IEnumerable<object> instances)> _runningPlugins = new();
        SingleFireFileWatcher _pluginChangesWatcher;
        readonly object[] plugingArgs = Array.Empty<object>();
        readonly CommandHandler handler;
#pragma warning disable IDE0052 // Remove unread private members - used implicitely to handle commands
        readonly UpdatePluginsCommand updatePluginsCommand;
#pragma warning restore IDE0052 // Remove unread private members

        public PluginsLoader(CommandHandler handler, Func<(string pluginName, string targetFolder), Task> pluginDownloader = null, string targetFolder = "plugins")
        {
            this.handler = handler;
            this.targetFolder = targetFolder; 
            if (pluginDownloader != null)
                this.updatePluginsCommand = new UpdatePluginsCommand(handler, pluginDownloader, this, targetFolder);
        }

        public void LoadPlugins(bool automaticallyLoadPluginChanges, bool preventLoadOfDecisionPlugins = false)
        {
            Directory.CreateDirectory(targetFolder);
            // load all
            foreach (var assemblyFullPath in Directory.GetFiles(targetFolder, "*.dll"))
                LoadPlugin(assemblyFullPath, preventLoadOfDecisionPlugins);

            handler.AddDecisions(_runningPlugins.SelectMany(p => p.Value.instances.OfType<LoopControlDecision>()).ToList());

            if (automaticallyLoadPluginChanges)
            {
                _pluginChangesWatcher = new SingleFireFileWatcher(targetFolder, "*.dll");
                _pluginChangesWatcher.Deleted += s => UnloadPlugin(s);
                _pluginChangesWatcher.Changed += ReloadPlugin;
            }
        }

        public void UnloadPlugins()
        {
            foreach (var entry in _runningPlugins.ToList())
                UnloadPlugin(entry.Key, entry.Value);
            GC.Collect(); // triggers the unload of the assembly (after DoUnloadExtension we no longer have references to the instances)
        }

        bool LoadPlugin(string assemblyFullPath, bool preventLoadOfDecisionPlugins)
        {
            var (context, commands, decisions) = Load(assemblyFullPath, plugingArgs);
            if (commands.Count == 0 && decisions.Count == 0)
            {
                context.Unload();
                return true;
            }

            if (decisions.Count > 0 && preventLoadOfDecisionPlugins)
            {
                CALog.LogData(LogID.A, $"decision plugins do not support live unloaded, skipped loading {assemblyFullPath}");
                context.Unload();
                return false;
            }

            foreach (var plugin in commands)
                plugin.Initialize(new PluginsCommandHandler(handler), new PluginsLogger(plugin.Name));

            var allPlugins = commands.AsEnumerable<object>().Concat(decisions).ToList();
            _runningPlugins[assemblyFullPath] = (context, allPlugins);
            CALog.LogData(LogID.A, $"loaded plugins from {assemblyFullPath} - {string.Join(",", allPlugins.Select(e => e.GetType().Name))}");
            return true;
        }

        bool UnloadPlugin(string assemblyFullPath)
        {
            if (_runningPlugins.TryGetValue(assemblyFullPath, out var runningPluginEntry))
                return UnloadPlugin(assemblyFullPath, runningPluginEntry);
            
            CALog.LogData(LogID.A, $"running plugin not found: {Path.GetFileNameWithoutExtension(assemblyFullPath)}");
            return true;
        }

        bool UnloadPlugin(string assemblyFullPath, (AssemblyLoadContext ctx, IEnumerable<object> instances) entry)
        {
            if (entry.instances.OfType<LoopControlDecision>().Any())
            {
                CALog.LogData(LogID.A, $"decision plugins do not support live unloaded, skipped {assemblyFullPath}");
                return false;
            }

            foreach (var instance in entry.instances)
                (instance as IDisposable)?.Dispose();
            _runningPlugins.Remove(assemblyFullPath);
            entry.ctx.Unload();
            CALog.LogData(LogID.A, $"unloaded plugins from {assemblyFullPath}");
            return true;
        }

        /// <remarks>
        /// we wait for a second to (un)load the plugin, and ignore it if a new change comes during that time (because the Changed event is fired multiple times in normal situations).
        /// </remarks>
        void ReloadPlugin(string fullpath)
        {
            if (!UnloadPlugin(fullpath)) 
                return;
            LoadPlugin(fullpath, preventLoadOfDecisionPlugins: true);
        }

        static (AssemblyLoadContext context, List<LoopControlCommand> commands, List<LoopControlDecision> decisions) Load(string assemblyFullPath, params object[] args)
        {
            var (context, assembly) = LoadAssembly(assemblyFullPath);
            return (context, CreateInstances<LoopControlCommand>(assembly, args).ToList(), CreateInstances<LoopControlDecision>(assembly, args).ToList());
        }

        static (AssemblyLoadContext context, Assembly assembly) LoadAssembly(string assemblyFullPath)
        {
            var context = new PluginLoadContext(assemblyFullPath);
            using var fs = new FileStream(assemblyFullPath, FileMode.Open, FileAccess.Read); // force no file lock
            return (context, context.LoadFromStream(fs));
        }

        static IEnumerable<T> CreateInstances<T>(Assembly assembly, params object[] args)
        {
            foreach (Type type in assembly.GetTypes())
            {
                if (!typeof(T).IsAssignableFrom(type)) continue;
                var result = (T) Activator.CreateInstance(type, args);
                yield return result;
            }
        }

        private class PluginLoadContext : AssemblyLoadContext
        {
            private readonly AssemblyDependencyResolver _resolver;

            public PluginLoadContext(string pluginFullPath) : base (true) => 
                _resolver = new AssemblyDependencyResolver(pluginFullPath);

            protected override Assembly Load(AssemblyName assemblyName)
            {
                string assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
                return assemblyPath != null ? LoadFromAssemblyPath(assemblyPath) : null;
            }

            protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
            {
                string libraryPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
                return libraryPath != null ? LoadUnmanagedDllFromPath(libraryPath) : IntPtr.Zero;
            }
        }

        /// <summary>a file watcher that avoids the multi fire issue of <see cref="FileSystemWatcher"/> (waits up to 1 second without changes before raising the file changed event)</summary>
        private class SingleFireFileWatcher
        {
            private const int MillisecondsWithoutChanges = 1000;
            readonly FileSystemWatcher watcher;
            readonly Dictionary<string, object> _postponedChangeLocks = new();
            public delegate void FileChangedDelegate(string fullpath);
            public event FileChangedDelegate Changed;
            public event FileChangedDelegate Deleted;
            public SingleFireFileWatcher(string folderPath, string filePattern)
            {
                //note: we ignore manual direct renames of the plugin assemblies files
                watcher = new FileSystemWatcher(folderPath, filePattern);
                watcher.Deleted += OnDeleted;
                watcher.Changed += OnChanged; // also handles creates
                watcher.EnableRaisingEvents = true;
            }

            private void OnDeleted(object sender, FileSystemEventArgs e) => Deleted?.Invoke(e.FullPath);

            private void OnChanged(object sender, FileSystemEventArgs e)
            {
                var mylock = new object();
                _postponedChangeLocks[e.FullPath] = mylock;
                var timer = new Timer(DelayedChange, (mylock, path: e.FullPath), MillisecondsWithoutChanges, Timeout.Infinite);
            }

            void DelayedChange(object state)
            {
                var (myDelayedLock, path) = ((object, string))state;
                if (!_postponedChangeLocks.TryGetValue(path, out var storedLock) || myDelayedLock != storedLock)
                    return;

                _postponedChangeLocks.Remove(path);
                Changed?.Invoke(path);
            }

        }

        /// <remarks>
        /// This command is not supported on multi node deployments. Additionally it does not support decisions plugins.
        /// </remarks>
        private class UpdatePluginsCommand : LoopControlCommand
        {
            private readonly Func<(string pluginName, string targetFolder), Task> pluginDownloader;
            private readonly PluginsLoader loader;
            private readonly string targetFolder;

            public override string Name => "updateplugins";

            public override string Description => "when no name is given it updates all existing plugins, otherwise adds/updates the specified plugin";

            public override bool IsHiddenCommand => true;

            public UpdatePluginsCommand(CommandHandler cmd, Func<(string pluginName, string targetFolder), Task> pluginDownloader, PluginsLoader loader, string targetFolder)
            {
                Initialize(new PluginsCommandHandler(cmd), new PluginsLogger("PluginsUpdater"));
                this.pluginDownloader = pluginDownloader;
                this.loader = loader;
                this.targetFolder = targetFolder;
            }

            protected override Task Command(List<string> args)
            {
                if (IOconfFile.GetEntries<IOconfNode>().Count() <= 1)
                    return args.Count > 1 ? UpdatePlugin(args[1]) : UpdateAllPlugins();

                CALog.LogErrorAndConsoleLn(LogID.A, "updateplugins is not supported in multipi deployments");
                return Task.CompletedTask;
            }

            private async Task UpdateAllPlugins()
            {
                foreach (var plugin in loader._runningPlugins)
                {
                    var pluginName = Path.GetFileNameWithoutExtension(plugin.Key);
                    if (plugin.Value.instances.OfType<LoopControlDecision>().Any())
                    {
                        CALog.LogInfoAndConsoleLn(LogID.A, $"decision plugins do not support live updates, skipped: {pluginName}");
                        continue;
                    }

                    CALog.LogInfoAndConsoleLn(LogID.A, $"downloading plugin: {pluginName}");
                    await pluginDownloader((pluginName, targetFolder));
                }

                CALog.LogInfoAndConsoleLn(LogID.A, $"unloading running plugins");
                loader.UnloadPlugins();
                CALog.LogInfoAndConsoleLn(LogID.A, $"loading plugins");
                loader.LoadPlugins(false, preventLoadOfDecisionPlugins: true);
                CALog.LogInfoAndConsoleLn(LogID.A, $"plugins updated");
            }

            private async Task UpdatePlugin(string pluginName)
            {
                if (loader._runningPlugins.Any(p => p.Key == pluginName && p.Value.instances.OfType<LoopControlDecision>().Any()))
                {
                    CALog.LogInfoAndConsoleLn(LogID.A, $"decision plugins do not support live updates, skipped: {pluginName}");
                    return;
                }

                CALog.LogInfoAndConsoleLn(LogID.A, $"downloading plugin: {pluginName}");
                await pluginDownloader((pluginName, targetFolder));
                var assemblyFullPath = Path.GetFullPath(Path.Combine(targetFolder, pluginName + ".dll"));
                CALog.LogInfoAndConsoleLn(LogID.A, $"unloading plugin: {pluginName}");
                if (!loader.UnloadPlugin(assemblyFullPath))
                    return;
                CALog.LogInfoAndConsoleLn(LogID.A, $"loading plugin: {pluginName}");
                if (!loader.LoadPlugin(assemblyFullPath, preventLoadOfDecisionPlugins: true))
                    return;
                CALog.LogInfoAndConsoleLn(LogID.A, $"plugins updated");
            }
        }
    }
}
