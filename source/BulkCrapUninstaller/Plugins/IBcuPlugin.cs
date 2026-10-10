/*
    Copilot extensibility experiment for Bulk Crap Uninstaller.
    Apache License Version 2.0 (matches the rest of the project)
*/

using System.Collections.Generic;
using UninstallTools;

namespace BulkCrapUninstaller.Plugins
{
    /// <summary>
    ///     Contract for plugins discovered in the "Plugins" directory next to the executable.
    ///     A plugin is any public type implementing this interface inside any *.dll placed there.
    /// </summary>
    public interface IBcuPlugin
    {
        /// <summary>Unique plugin name used to address it from the control plane.</summary>
        string Name { get; }

        /// <summary>Short human readable description.</summary>
        string Description { get; }

        /// <summary>Called once after the main window is shown.</summary>
        void Initialize(IBcuPluginHost host);

        /// <summary>
        ///     Handle a command sent through the control plane (POST /plugin).
        ///     Return null if the command is not recognized by this plugin.
        /// </summary>
        string HandleCommand(string command, string payload);
    }

    /// <summary>
    ///     Bridge between a plugin and the running application. Methods may be called from any
    ///     thread; implementations are responsible for marshaling UI work to the UI thread.
    /// </summary>
    public interface IBcuPluginHost
    {
        /// <summary>Snapshot of every known uninstaller entry.</summary>
        IReadOnlyList<ApplicationUninstallerEntry> AllEntries { get; }

        /// <summary>Snapshot of the entries currently visible in the list (after filters).</summary>
        IReadOnlyList<ApplicationUninstallerEntry> FilteredEntries { get; }

        /// <summary>Select (and check, if checkboxes are enabled) a single entry in the list.</summary>
        void SelectEntry(ApplicationUninstallerEntry entry);

        /// <summary>Start a full list refresh.</summary>
        void RefreshList();

        /// <summary>Write a message to the status bar.</summary>
        void ShowStatus(string message);
    }
}