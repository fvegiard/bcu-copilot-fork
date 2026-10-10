/*
    Copilot extensibility experiment for Bulk Crap Uninstaller.
    Apache License Version 2.0 (matches the rest of the project)
*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Windows.Forms;
using BulkCrapUninstaller.Forms;
using UninstallTools;

namespace BulkCrapUninstaller.Plugins
{
    /// <summary>
    ///     Minimal localhost HTTP control plane (TcpListener based, no http.sys) that exposes
    ///     read-only application data, list selection and plugin commands as JSON.
    ///     Endpoints: GET /status, GET /apps?query=&amp;limit=, POST /select, POST /plugin, GET /logtail?lines=
    /// </summary>
    internal static class ControlPlaneServer
    {
        private const int DefaultPort = 47613;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private static MainWindow _main;
        private static IReadOnlyList<IBcuPlugin> _plugins = Array.Empty<IBcuPlugin>();
        private static DateTime _startedAt;

        internal static void Start(MainWindow main, IReadOnlyList<IBcuPlugin> plugins)
        {
            _main = main;
            _plugins = plugins;
            _startedAt = DateTime.UtcNow;

            var port = ParsePort();
            var thread = new Thread(() => ListenLoop(port))
            {
                IsBackground = true,
                Name = "BcuControlPlane"
            };
            thread.Start();
        }

        private static int ParsePort()
        {
            var raw = Environment.GetEnvironmentVariable("BCU_CONTROL_PORT");
            return int.TryParse(raw, out var port) && port > 0 && port < 65536 ? port : DefaultPort;
        }

        private static void ListenLoop(int port)
        {
            try
            {
                var listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
                Console.WriteLine($"[ControlPlane] Listening on http://127.0.0.1:{port}/");
                while (true)
                {
                    TcpClient client;
                    try { client = listener.AcceptTcpClient(); }
                    catch (SocketException) { break; }

                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        try { HandleClient(client); }
                        catch (Exception ex) { Console.WriteLine("[ControlPlane] Request failed: " + ex.Message); }
                        finally { try { client.Close(); } catch { /* ignore */ } }
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[ControlPlane] Failed to start: " + ex.Message);
            }
        }

        private static void HandleClient(TcpClient client)
        {
            using (client)
            using (var stream = client.GetStream())
            {
                stream.ReadTimeout = 5000;
                var request = ReadRequest(stream);
                if (request == null)
                {
                    WriteResponse(stream, 400, JsonError("Malformed request"));
                    return;
                }

                var (status, body) = Route(request.Value);
                WriteResponse(stream, status, body);
            }
        }

        private struct HttpRequest
        {
            public string Method;
            public string Path;
            public string Query;
            public string Body;
        }

        private static HttpRequest? ReadRequest(NetworkStream stream)
        {
            var buffer = new byte[8192];
            using var data = new MemoryStream();
            var headerEnd = -1L;

            while (headerEnd < 0)
            {
                var read = stream.Read(buffer, 0, buffer.Length);
                if (read <= 0) return null;
                data.Write(buffer, 0, read);
                headerEnd = FindHeaderEnd(data);
                if (data.Length > 1024 * 1024) return null;
            }

            var all = data.ToArray();
            var headerText = Encoding.ASCII.GetString(all, 0, (int)headerEnd);
            var lines = headerText.Split(new[] { "\r\n" }, StringSplitOptions.None);
            var requestLine = lines[0].Split(' ');
            if (requestLine.Length < 2) return null;

            var contentLength = 0;
            foreach (var line in lines.Skip(1))
            {
                var idx = line.IndexOf(':');
                if (idx <= 0) continue;
                if (line.Substring(0, idx).Trim().Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                    int.TryParse(line.Substring(idx + 1).Trim(), out contentLength);
            }

            var bodyStart = (int)headerEnd + 4;
            using var body = new MemoryStream();
            if (contentLength > 0)
            {
                var take = Math.Min(all.Length - bodyStart, contentLength);
                if (take > 0) body.Write(all, bodyStart, take);
                while (body.Length < contentLength)
                {
                    var read = stream.Read(buffer, 0, Math.Min(buffer.Length, contentLength - (int)body.Length));
                    if (read <= 0) break;
                    body.Write(buffer, 0, read);
                }
            }

            var pathAndQuery = requestLine[1];
            var queryIdx = pathAndQuery.IndexOf('?');
            return new HttpRequest
            {
                Method = requestLine[0].ToUpperInvariant(),
                Path = queryIdx < 0 ? pathAndQuery : pathAndQuery.Substring(0, queryIdx),
                Query = queryIdx < 0 ? string.Empty : pathAndQuery.Substring(queryIdx + 1),
                Body = Encoding.UTF8.GetString(body.ToArray())
            };
        }

        private static long FindHeaderEnd(MemoryStream data)
        {
            var bytes = data.GetBuffer();
            var length = (int)data.Length;
            for (var i = 0; i + 3 < length; i++)
                if (bytes[i] == 13 && bytes[i + 1] == 10 && bytes[i + 2] == 13 && bytes[i + 3] == 10)
                    return i;
            return -1;
        }

        private static (int status, string body) Route(HttpRequest request)
        {
            try
            {
                switch (request.Path)
                {
                    case "/status" when request.Method == "GET":
                        return (200, GetStatus());
                    case "/apps" when request.Method == "GET":
                        return (200, GetApps(request.Query));
                    case "/select" when request.Method == "POST":
                        return (200, PostSelect(request.Body));
                    case "/plugin" when request.Method == "POST":
                        return (200, PostPlugin(request.Body));
                    case "/logtail" when request.Method == "GET":
                        return (200, GetLogTail(request.Query));
                    default:
                        return (404, JsonError($"Unknown endpoint: {request.Method} {request.Path}"));
                }
            }
            catch (Exception ex)
            {
                return (500, JsonError(ex.ToString()));
            }
        }

        private static string GetStatus()
        {
            var list = OnUi(() => new
            {
                all = _main.ListView.AllUninstallers.Count(),
                filtered = _main.ListView.FilteredUninstallers.Count(),
                selected = _main.ListView.SelectedUninstallers.Select(e => e.DisplayName).ToList(),
                refreshRunning = _main.ListView.ListRefreshIsRunning,
                firstRefreshCompleted = _main.ListView.FirstRefreshCompleted
            });

            var payload = new
            {
                app = "Bulk Crap Uninstaller (Copilot fork)",
                version = Application.ProductVersion,
                controlPlane = new { port = ParsePort(), startedAt = _startedAt.ToString("o") },
                list,
                plugins = _plugins.Select(p => new { p.Name, p.Description })
            };
            return JsonSerializer.Serialize(payload, JsonOptions);
        }

        private static string GetApps(string query)
        {
            var (q, limit) = ParseQuery(query);
            var entries = _main.ListView.AllUninstallers
                .Where(e => q == null ||
                            (e.DisplayName?.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) ||
                            (e.Publisher?.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0))
                .OrderBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Take(limit)
                .Select(e => new
                {
                    name = e.DisplayName,
                    version = e.DisplayVersion,
                    publisher = e.Publisher,
                    kind = e.UninstallerKind.ToString(),
                    size = e.EstimatedSize.ToString(),
                    installLocation = e.InstallLocation,
                    installDate = e.InstallDate.ToString("yyyy-MM-dd"),
                    orphaned = e.IsOrphaned,
                    update = e.IsUpdate,
                    valid = e.IsValid,
                    registered = e.IsRegistered,
                    systemComponent = e.SystemComponent,
                    ratingId = e.RatingId
                })
                .ToList();
            return JsonSerializer.Serialize(new { count = entries.Count, entries }, JsonOptions);
        }

        private static string PostSelect(string body)
        {
            using var doc = JsonDocument.Parse(body.Length > 0 ? body : "{}");
            var name = doc.RootElement.ValueKind == JsonValueKind.Object &&
                       doc.RootElement.TryGetProperty("name", out var nameEl) &&
                       nameEl.ValueKind == JsonValueKind.String
                ? nameEl.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(name))
                return JsonError("Body must be JSON: {\"name\": \"...\"}");

            var target = FindEntry(name);
            if (target == null)
                return JsonSerializer.Serialize(new { ok = false, error = $"No entry matching '{name}'" }, JsonOptions);

            OnUi(() => _main.ListView.SelectEntry(target));
            return JsonSerializer.Serialize(new { ok = true, selected = target.DisplayName }, JsonOptions);
        }

        private static ApplicationUninstallerEntry FindEntry(string name)
        {
            var all = _main.ListView.AllUninstallers.ToList();
            var exact = all.FirstOrDefault(e =>
                string.Equals(e.DisplayName, name, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;
            return all.FirstOrDefault(e => e.DisplayName?.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string PostPlugin(string body)
        {
            using var doc = JsonDocument.Parse(body.Length > 0 ? body : "{}");
            string pluginName = null, command = null, payload = null;
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (doc.RootElement.TryGetProperty("plugin", out var p) && p.ValueKind == JsonValueKind.String) pluginName = p.GetString();
                if (doc.RootElement.TryGetProperty("command", out var c) && c.ValueKind == JsonValueKind.String) command = c.GetString();
                if (doc.RootElement.TryGetProperty("payload", out var pl))
                    payload = pl.ValueKind == JsonValueKind.String ? pl.GetString() : pl.GetRawText();
            }
            if (string.IsNullOrWhiteSpace(pluginName) || string.IsNullOrWhiteSpace(command))
                return JsonError("Body must be JSON: {\"plugin\": \"...\", \"command\": \"...\", \"payload\": \"...\"}");

            var plugin = _plugins.FirstOrDefault(x => string.Equals(x.Name, pluginName, StringComparison.OrdinalIgnoreCase));
            if (plugin == null)
                return JsonSerializer.Serialize(new
                {
                    ok = false,
                    error = $"Plugin '{pluginName}' not loaded",
                    loaded = _plugins.Select(x => x.Name)
                }, JsonOptions);

            var result = plugin.HandleCommand(command, payload);
            if (result == null)
                return JsonSerializer.Serialize(new
                {
                    ok = false,
                    error = $"Plugin '{pluginName}' does not handle command '{command}'"
                }, JsonOptions);

            return JsonSerializer.Serialize(new { ok = true, plugin = plugin.Name, command, result }, JsonOptions);
        }

        private static string GetLogTail(string query)
        {
            var lines = 30;
            if (!string.IsNullOrEmpty(query))
                foreach (var pair in query.Split('&'))
                {
                    var idx = pair.IndexOf('=');
                    if (idx > 0 && pair.Substring(0, idx) == "lines" &&
                        int.TryParse(pair.Substring(idx + 1), out var l) && l > 0)
                        lines = Math.Min(l, 500);
                }

            var logPath = Path.Combine(Program.AssemblyLocation.FullName, "BCUninstaller.log");
            if (!File.Exists(logPath))
                return JsonSerializer.Serialize(new { log = Array.Empty<string>() }, JsonOptions);

            // The log is held open by the running process, so it must be opened with shared access
            var buffer = new Queue<string>();
            using (var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    buffer.Enqueue(line);
                    if (buffer.Count > lines) buffer.Dequeue();
                }
            }

            return JsonSerializer.Serialize(new { log = buffer.ToArray() }, JsonOptions);
        }

        private static (string query, int limit) ParseQuery(string raw)
        {
            string q = null;
            var limit = 50;
            if (!string.IsNullOrEmpty(raw))
            {
                foreach (var pair in raw.Split('&'))
                {
                    var idx = pair.IndexOf('=');
                    if (idx < 0) continue;
                    var key = pair.Substring(0, idx);
                    var value = Uri.UnescapeDataString(pair.Substring(idx + 1).Replace("+", " "));
                    if (key == "query" && value.Length > 0) q = value;
                    else if (key == "limit" && int.TryParse(value, out var l) && l > 0) limit = Math.Min(l, 500);
                }
            }
            return (q, limit);
        }

        private static void WriteResponse(NetworkStream stream, int status, string body)
        {
            var bodyBytes = Encoding.UTF8.GetBytes(body);
            var header = $"HTTP/1.1 {status} {GetStatusText(status)}\r\n" +
                         "Content-Type: application/json; charset=utf-8\r\n" +
                         $"Content-Length: {bodyBytes.Length}\r\n" +
                         "Connection: close\r\n\r\n";
            var headerBytes = Encoding.ASCII.GetBytes(header);
            stream.Write(headerBytes, 0, headerBytes.Length);
            stream.Write(bodyBytes, 0, bodyBytes.Length);
            stream.Flush();
        }

        private static string GetStatusText(int status)
        {
            switch (status)
            {
                case 200: return "OK";
                case 400: return "Bad Request";
                case 404: return "Not Found";
                case 500: return "Internal Server Error";
                default: return "OK";
            }
        }

        private static string JsonError(string message)
        {
            return JsonSerializer.Serialize(new { ok = false, error = message }, JsonOptions);
        }

        private static T OnUi<T>(Func<T> func)
        {
            var main = _main;
            if (main == null || main.IsDisposed || !main.IsHandleCreated) return default;
            return (T)main.Invoke(func);
        }

        private static void OnUi(Action action)
        {
            var main = _main;
            if (main == null || main.IsDisposed || !main.IsHandleCreated) return;
            main.Invoke(action);
        }
    }
}