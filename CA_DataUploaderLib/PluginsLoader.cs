using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using CA.LoopControlPluginBase;

namespace CA_DataUploaderLib
{
    public class PluginsLoader
    { // see https://docs.microsoft.com/en-us/dotnet/core/tutorials/creating-app-with-plugin-support
        readonly Dictionary<string, (AssemblyLoadContext ctx, IEnumerable<LoopControlCommand> instances)> _runningPlugins =
            new Dictionary<string, (AssemblyLoadContext ctx, IEnumerable<LoopControlCommand> instances)>();
        SingleFireFileWatcher _pluginChangesWatcher;
        readonly object[] plugingArgs = {};
        readonly CommandHandler handler;
        UpdatePluginsCommand updatePluginsCommand;

        public PluginsLoader(CommandHandler handler, Func<(string pluginName, string targetFolder), Task> pluginDownloader = null)
        {
            this.handler = handler;
            if (pluginDownloader != null)
                this.updatePluginsCommand = new UpdatePluginsCommand(handler, pluginDownloader, this);
        }

        public IEnumerable<string> GetRunningPluginsNames() => _runningPlugins.Keys.Select(v => Path.GetFileName(v));

        public void LoadPlugins(bool automaticallyLoadPluginChanges = true)
        {
            // load all
            foreach (var assembly in Directory.GetFiles(".", "*.dll"))
                LoadPlugin(assembly);

            if (automaticallyLoadPluginChanges)
            {
                _pluginChangesWatcher = new SingleFireFileWatcher(".", "*.dll");
                _pluginChangesWatcher.Deleted += UnloadPlugin;
                _pluginChangesWatcher.Changed += ReloadPlugin;
            }
        }

        public void UnloadPlugins()
        {
            foreach (var entry in _runningPlugins.ToList())
                UnloadPlugin(entry.Key, entry.Value);
            GC.Collect(); // triggers the unload of the assembly (after DoUnloadExtension we no longer have references to the instances)
        }

        void LoadPlugin(string assemblyPath)
        {
            assemblyPath = Path.GetFullPath(assemblyPath);
            var (context, plugins) = Load(assemblyPath, plugingArgs);
            var initializedPlugins = plugins.ToList();
            if (initializedPlugins.Count == 0)
            {
                context.Unload();
                return;
            }

            foreach (var plugin in initializedPlugins)
                plugin.Initialize(new PluginsCommandHandler(handler), new PluginsLogger(plugin.Name)); 

            _runningPlugins[assemblyPath] = (context, initializedPlugins);
            CALog.LogData(LogID.A, $"loaded plugins from {assemblyPath} - {string.Join(",", initializedPlugins.Select(e => e.GetType().Name))}");
        }

        void UnloadPlugin(string assemblyPath)
        {
            assemblyPath = Path.GetFullPath(assemblyPath);
            if (!_runningPlugins.TryGetValue(assemblyPath, out var runningPluginEntry))
                CALog.LogData(LogID.A, $"running plugin not found: {Path.GetFileName(assemblyPath)}");
            else
                UnloadPlugin(assemblyPath, runningPluginEntry);
        }

        void UnloadPlugin(string assemblyPath, (AssemblyLoadContext ctx, IEnumerable<LoopControlCommand> instances) entry)
        {
            foreach (var instance in entry.instances)
                instance.Dispose();
            _runningPlugins.Remove(assemblyPath);
            entry.ctx.Unload();
            CALog.LogData(LogID.A, $"unloaded plugins from {assemblyPath}");
        }

        /// <remarks>
        /// we wait for a second to (un)load the plugin, and ignore it if a new change comes during that time (because the Changed event is fired multiple times in normal situations).
        /// </remarks>
        void ReloadPlugin(string fullpath)
        {
            UnloadPlugin(fullpath);
            LoadPlugin(fullpath);
        }

        static (AssemblyLoadContext context, IEnumerable<LoopControlCommand> plugins) Load(string assemblyPath, params object[] args)
        {
            var (context, assembly) = LoadAssembly(assemblyPath);
            return (context, CreateInstances<LoopControlCommand>(assembly, args));
        }

        static (AssemblyLoadContext context, Assembly assembly) LoadAssembly(string assemblyPath)
        {
            PluginLoadContext context = new PluginLoadContext(assemblyPath);
            using var fs = new FileStream(assemblyPath, FileMode.Open, FileAccess.Read); // force no file lock
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

            public PluginLoadContext(string pluginPath) : base (true) => 
                _resolver = new AssemblyDependencyResolver(pluginPath);

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
            readonly Dictionary<string, object> _postponedChangeLocks = new Dictionary<string, object>();
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

        private class UpdatePluginsCommand : LoopControlCommand
        {
            private readonly Func<(string pluginName, string targetFolder), Task> pluginDownloader;
            private readonly PluginsLoader loader;

            public override string Name => "updateplugins";

            public override string Description => "when no name is given it updates all existing plugins, otherwise adds/updates the specified plugin";

            public override bool IsHiddenCommand => true;

            public UpdatePluginsCommand(CommandHandler cmd, Func<(string pluginName, string targetFolder), Task> pluginDownloader, PluginsLoader loader)
            {
                Initialize(new PluginsCommandHandler(cmd), new PluginsLogger("PluginsUpdater"));
                this.pluginDownloader = pluginDownloader;
                this.loader = loader;
            }

            protected override Task Command(List<string> args) => args.Count > 1 ? UpdatePlugin(args[1]) : UpdateAllPlugins();

            private async Task UpdateAllPlugins()
            {
                foreach (var plugin in loader.GetRunningPluginsNames())
                {
                    var pluginName = Path.GetFileName(plugin);
                    CALog.LogInfoAndConsoleLn(LogID.A, $"downloading plugin: {pluginName}");
                    await pluginDownloader((pluginName, "."));
                }

                CALog.LogInfoAndConsoleLn(LogID.A, $"unloading running plugins");
                loader.UnloadPlugins();
                CALog.LogInfoAndConsoleLn(LogID.A, $"loading plugins");
                loader.LoadPlugins();
                CALog.LogInfoAndConsoleLn(LogID.A, $"plugins updated");
            }

            private async Task UpdatePlugin(string pluginName)
            {
                CALog.LogInfoAndConsoleLn(LogID.A, $"downloading plugin: {pluginName}");
                await pluginDownloader((pluginName, "."));
                var assemblyPath = Path.GetFullPath(pluginName + ".dll");
                CALog.LogInfoAndConsoleLn(LogID.A, $"unloading plugin: {pluginName}");
                loader.UnloadPlugin(assemblyPath);
                CALog.LogInfoAndConsoleLn(LogID.A, $"loading plugin: {pluginName}");
                loader.LoadPlugin(assemblyPath);
                CALog.LogInfoAndConsoleLn(LogID.A, $"plugins updated");
            }
        }
    }
}
