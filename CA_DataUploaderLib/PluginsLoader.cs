using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using CA.LoopControlPluginBase;
using CA_DataUploaderLib.IOconf;

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
            var confDecisions = IOconfFile.GetEntries<IOconfCode>();
            foreach (Type type in assembly.GetTypes())
            {
                if (!typeof(T).IsAssignableFrom(type)) continue;

                foreach (var confDecision in confDecisions.Where(cd => cd.ClassName == type.Name))
                {
                    var result =
                        (confDecision.Name == confDecision.ClassName
                         ? (T?)Activator.CreateInstance(type, args)
                         : (T?)Activator.CreateInstance(type, new[] { confDecision.Name }.Concat(args).ToArray()))
                        ?? throw new InvalidOperationException($"Activator.CreateInstance returned null for {type.FullName}");
                    yield return result;
                }
            }
        }

        private class PluginLoadContext : AssemblyLoadContext
        {
            private readonly AssemblyDependencyResolver _resolver;

            public PluginLoadContext(string pluginFullPath) : base (true) => 
                _resolver = new AssemblyDependencyResolver(pluginFullPath);

            protected override Assembly? Load(AssemblyName assemblyName)
            {
                string? assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
                return assemblyPath != null ? LoadFromAssemblyPath(assemblyPath) : null;
            }

            protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
            {
                string? libraryPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
                return libraryPath != null ? LoadUnmanagedDllFromPath(libraryPath) : IntPtr.Zero;
            }
        }
    }
}
