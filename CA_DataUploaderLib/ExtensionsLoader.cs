using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace CA_DataUploaderLib
{
    public class ExtensionsLoader
    { // see https://docs.microsoft.com/en-us/dotnet/core/tutorials/creating-app-with-plugin-support
        public static (AssemblyLoadContext context, IEnumerable<LoopControlExtension> extensions) Load(string assemblyPath, CommandHandler cmd)
        {
            var (context, assembly) = LoadPlugin(assemblyPath);
            return (context, CreateInstances<LoopControlExtension>(assembly, cmd));
        }

        static (AssemblyLoadContext context, Assembly assembly) LoadPlugin(string assemblyPath)
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
