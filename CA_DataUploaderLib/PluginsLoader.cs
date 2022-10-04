using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using CA.LoopControlPluginBase;

namespace CA_DataUploaderLib
{
    public class PluginsLoader
    { // see https://docs.microsoft.com/en-us/dotnet/core/tutorials/creating-app-with-plugin-support
        private readonly string targetFolder;
        readonly CommandHandler handler;

        public PluginsLoader(CommandHandler handler, string targetFolder = "plugins")
        {
            this.handler = handler;
            this.targetFolder = targetFolder; 
        }

        public void LoadPlugins()
        {
            Directory.CreateDirectory(targetFolder);
            // load all
            foreach (var assemblyFullPath in Directory.GetFiles(targetFolder, "*.dll"))
                LoadPlugin(assemblyFullPath);
        }

        void LoadPlugin(string assemblyFullPath)
        {
            var (context, assembly) = LoadAssembly(assemblyFullPath);
            var decisions = CreateInstances<LoopControlDecision>(assembly, Array.Empty<object>()).ToList();
            if (decisions.Count == 0)
            {
                context.Unload();
                return;
            }

            CALog.LogData(LogID.A, $"loaded plugins from {assemblyFullPath} - {string.Join(",", decisions.Select(e => e.GetType().Name))}");
            handler.AddDecisions(decisions);
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
    }
}
