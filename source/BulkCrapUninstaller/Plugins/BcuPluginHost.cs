/*
    Copilot extensibility experiment for Bulk Crap Uninstaller.
    Apache License Version 2.0 (matches the rest of the project)
*/

using System;
using System.Collections.Generic;
using System.Linq;
using BulkCrapUninstaller.Forms;
using UninstallTools;

namespace BulkCrapUninstaller.Plugins
{
    /// <summary>
    ///     Bridges plugin calls to the main window. Reads take a snapshot; UI mutations are
    ///     marshaled to the UI thread with BeginInvoke.
    /// </summary>
    internal class BcuPluginHost : IBcuPluginHost
    {
        private readonly MainWindow _mainWindow;

        internal BcuPluginHost(MainWindow mainWindow)
        {
            _mainWindow = mainWindow;
        }

        public IReadOnlyList<ApplicationUninstallerEntry> AllEntries =>
            _mainWindow.ListView.AllUninstallers.ToList().AsReadOnly();

        public IReadOnlyList<ApplicationUninstallerEntry> FilteredEntries =>
            _mainWindow.Invoke(new Func<IReadOnlyList<ApplicationUninstallerEntry>>(() =>
                _mainWindow.ListView.FilteredUninstallers.ToList().AsReadOnly()));

        public void SelectEntry(ApplicationUninstallerEntry entry)
        {
            if (entry == null || _mainWindow.IsDisposed) return;
            _mainWindow.BeginInvoke(new Action(() => _mainWindow.ListView.SelectEntry(entry)));
        }

        public void RefreshList()
        {
            if (_mainWindow.IsDisposed) return;
            _mainWindow.BeginInvoke(new Action(() => _mainWindow.ListView.InitiateListRefresh()));
        }

        public void ShowStatus(string message)
        {
            if (_mainWindow.IsDisposed) return;
            _mainWindow.BeginInvoke(new Action(() => _mainWindow.SetPluginStatus(message)));
        }
    }
}