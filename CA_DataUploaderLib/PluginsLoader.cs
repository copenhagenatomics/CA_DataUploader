using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;

namespace CA_DataUploaderLib
{
    public class PluginsLoader
    { // see https://docs.microsoft.com/en-us/dotnet/core/tutorials/creating-app-with-plugin-support
        readonly Dictionary<string, (AssemblyLoadContext ctx, IEnumerable<LoopControlPlugin> instances)> _runningPlugins =
            new Dictionary<string, (AssemblyLoadContext ctx, IEnumerable<LoopControlPlugin> instances)>();
        FileSystemWatcher _pluginChangesWatcher;
        readonly Dictionary<string, object> _postponedPluginChangeLocks = new Dictionary<string, object>();
        readonly CommandHandler handler;

        public static PluginsLoader AddCommands(CommandHandler handler) => new PluginsLoader(handler);
        private PluginsLoader(CommandHandler handler)
        {
            this.handler = handler;
            handler.AddCommand("Load", LoadPlugin);
            handler.AddCommand("Unload", UnloadPlugin);
        }

        private bool LoadPlugin(List<string> args)
        {
            var isAuto = args.Count > 1 && args[1] == "auto";
            if (args.Count < 2 || isAuto)
            {
                LoadPlugins(isAuto);
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

        private bool UnloadPlugin(List<string> args)
        {
            if (args.Count < 2)
            { // unload all
                UnloadPlugins();
                return true;
            }

            UnloadPlugin(args[1]);
            GC.Collect(); // triggers the unload of the assembly (after DoUnloadExtension we no longer have references to the instances)
            return true;
        }

        public void LoadPlugins(bool automaticallyLoadPluginChanges)
        {
            // load all
            foreach (var assembly in Directory.GetFiles(".", "*.dll"))
                LoadPlugin(assembly);

            if (automaticallyLoadPluginChanges)
                TrackPluginChanges("*.dll");
        }

        public void LoadPlugin(string assemblyPath)
        {
            assemblyPath = Path.GetFullPath(assemblyPath);
            var (context, plugins) = Load(assemblyPath, handler);
            var initializedPlugins = plugins.ToList(); // iterate the enumerable to create/initialize the instances
            if (initializedPlugins.Count == 0)
            {
                context.Unload();
                return;
            }

            _runningPlugins[assemblyPath] = (context, initializedPlugins);
            Console.WriteLine($"loaded plugins from {assemblyPath} - {string.Join(",", initializedPlugins.Select(e => e.GetType().Name))}");
        }

        public void UnloadPlugins()
        {
            foreach (var assembly in _runningPlugins.Keys.ToList())
                UnloadPlugin(assembly);
            GC.Collect(); // triggers the unload of the assembly (after DoUnloadExtension we no longer have references to the instances)
        }

        public bool UnloadPlugin(string assemblyPath)
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

        void TrackPluginChanges(string filepattern)
        {
            //created files is already handled by the changed event, while we ignore manual direct renames of the plugin assemblies files
            _pluginChangesWatcher = new FileSystemWatcher(".", filepattern);
            _pluginChangesWatcher.Deleted += OnAssembliesDeleted;
            _pluginChangesWatcher.Changed += OnAssembliesChanged;
            _pluginChangesWatcher.EnableRaisingEvents = true;
        }

        void OnAssembliesDeleted(object sender, FileSystemEventArgs e) => UnloadPlugin(e.FullPath);
        /// <remarks>
        /// we wait for a second to (un)load the plugin, and ignore it if a new change comes during that time (because the Changed event is fired multiple times in normal situations).
        /// </remarks>
        void OnAssembliesChanged(object sender, FileSystemEventArgs e)
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

        static (AssemblyLoadContext context, IEnumerable<LoopControlPlugin> plugins) Load(string assemblyPath, CommandHandler cmd)
        {
            var (context, assembly) = LoadAssembly(assemblyPath);
            return (context, CreateInstances<LoopControlPlugin>(assembly, cmd));
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

    }
}
