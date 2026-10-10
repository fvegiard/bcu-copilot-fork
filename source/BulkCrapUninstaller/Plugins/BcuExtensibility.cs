/*
    Copilot extensibility experiment for Bulk Crap Uninstaller.
    Apache License Version 2.0 (matches the rest of the project)
*/

using System;
using System.IO;
using System.Linq;

namespace BulkCrapUninstaller.Plugins
{
    /// <summary>
    ///     Single startup hook for the fork's extensibility experiment: loads plugins from
    ///     "&lt;exe dir&gt;\Plugins" and starts the localhost control plane.
    /// </summary>
    internal static class BcuExtensibility
    {
        internal static void Start(BulkCrapUninstaller.Forms.MainWindow main)
        {
            try
            {
                var pluginsDir = Path.Combine(Program.AssemblyLocation.FullName, "Plugins");
                var host = new BcuPluginHost(main);
                var plugins = PluginLoader.LoadPlugins(host, pluginsDir);
                Console.WriteLine(plugins.Count == 0
                    ? "[Plugins] No plugins found in " + pluginsDir
                    : "[Plugins] Loaded: " + string.Join(", ", plugins.Select(p => p.Name)));

                ControlPlaneServer.Start(main, plugins);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[Plugins] Extensibility startup failed: " + ex);
            }
        }
    }
}