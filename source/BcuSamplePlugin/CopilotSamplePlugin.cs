/*
    Copilot extensibility experiment for Bulk Crap Uninstaller.
    Apache License Version 2.0 (matches the rest of the project)
*/

using System;
using System.Linq;
using BulkCrapUninstaller.Plugins;

namespace BcuSamplePlugin
{
    /// <summary>
    ///     Sample plugin proving in-process extensibility: it runs inside the BCU process,
    ///     sees the full uninstaller list and answers commands from the control plane.
    /// </summary>
    public class CopilotSamplePlugin : IBcuPlugin
    {
        private IBcuPluginHost _host;

        public string Name => "CopilotSample";
        public string Description => "Sample plugin: echo / count / find commands, proves the plugin loader works.";

        public void Initialize(IBcuPluginHost host)
        {
            _host = host;
            Console.WriteLine("[CopilotSamplePlugin] Initialized inside the BCU process.");
        }

        public string HandleCommand(string command, string payload)
        {
            switch (command)
            {
                case "echo":
                    return payload ?? string.Empty;

                case "count":
                    return _host.AllEntries.Count.ToString();

                case "find":
                {
                    var q = (payload ?? string.Empty).Trim();
                    if (q.Length == 0) return "Usage: find <text>";
                    var hits = _host.AllEntries
                        .Where(e => e.DisplayName?.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                        .Select(e => e.DisplayName)
                        .Take(10)
                        .ToList();
                    return hits.Count == 0 ? $"No entries matching '{q}'" : string.Join(" | ", hits);
                }

                default:
                    return null;
            }
        }
    }
}