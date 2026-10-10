/*
    Copilot extensibility experiment for Bulk Crap Uninstaller.
    Apache License Version 2.0 (matches the rest of the project)
*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace BulkCrapUninstaller.Plugins
{
    internal static class PluginLoader
    {
        internal static IReadOnlyList<IBcuPlugin> LoadPlugins(IBcuPluginHost host, string pluginsDirectory)
        {
            var loaded = new List<IBcuPlugin>();
            if (!Directory.Exists(pluginsDirectory))
                return loaded;

            foreach (var dllPath in Directory.GetFiles(pluginsDirectory, "*.dll"))
            {
                Assembly assembly;
                try
                {
                    assembly = Assembly.LoadFrom(dllPath);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Plugins] Failed to load '{Path.GetFileName(dllPath)}': {ex.Message}");
                    continue;
                }

                IBcuPlugin[] found;
                try
                {
                    found = assembly.GetTypes()
                        .Where(t => !t.IsAbstract && !t.IsInterface && typeof(IBcuPlugin).IsAssignableFrom(t))
                        .Select(t => (IBcuPlugin)Activator.CreateInstance(t))
                        .ToArray();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Plugins] Failed to inspect '{Path.GetFileName(dllPath)}': {ex.Message}");
                    continue;
                }

                foreach (var plugin in found)
                {
                    try
                    {
                        plugin.Initialize(host);
                        loaded.Add(plugin);
                        Console.WriteLine($"[Plugins] Loaded '{plugin.Name}' from '{Path.GetFileName(dllPath)}'");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Plugins] Failed to initialize '{plugin.GetType().FullName}': {ex.Message}");
                    }
                }
            }

            return loaded;
        }
    }
}