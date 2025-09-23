using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using BepInEx;
using UnityEngine;

namespace Dashboard
{
    public sealed class LocalHttpServer
    {
        // Simple 1x1 colored PNG for placeholders
        private static readonly byte[] PlaceholderPng = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR4nGMAAQAABQABDQottQAAAABJRU5ErkJggg==");
        
        // Simple 16x16 colored PNG for NPC photos
        private static readonly byte[] DefaultNpcPng = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAABAAAAAQCAIAAACQkWg2AAAACXBIWXMAAA7EAAAOxAGVKw4bAAAAB3RJTUUH4wIIChAky9/vJAAAAChJREFUOMtjZGBgYBgFwwwwMTAwMDIyMjEwMDAxMTEzMzOPhoZhAADLrAEVZF+6PAAAAABJRU5ErkJggg==");
        private static readonly object _sync = new object();
        private static LocalHttpServer _instance;
        public static LocalHttpServer Instance
        {
            get
            {
                lock (_sync)
                {
                    return _instance ??= new LocalHttpServer();
                }
            }
        }

        private TcpListener _listener;
        private CancellationTokenSource _cts;
        private string _wwwRoot;
        private int _port;
        private bool _running;

        private LocalHttpServer() { }

        public bool IsRunning => _running;
        public int Port => _port;
        public string WebRoot => _wwwRoot;

        public void Start(int port, string wwwRoot)
        {
            if (_running) return;
            _port = port;
            _wwwRoot = wwwRoot;

            try
            {
                if (!Directory.Exists(_wwwRoot))
                {
                    Directory.CreateDirectory(_wwwRoot);
                }

                _cts = new CancellationTokenSource();

                _listener = new TcpListener(IPAddress.Loopback, _port);
                _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _listener.Start();
                _running = true;

                Thread thread = new Thread(() => AcceptLoop(_cts.Token))
                {
                    IsBackground = true,
                    Name = "Dashboard-HttpServer"
                };
                thread.Start();

                ModLogger.Info($"Server listening on http://127.0.0.1:{_port} serving '{_wwwRoot}'");
            }
            catch (Exception ex)
            {
                ModLogger.Error($"Failed to start server on port {_port}: {ex}");
                Stop();
            }
        }

        public void Stop()
        {
            try
            {
                _running = false;
                _cts?.Cancel();
                _listener?.Stop();
            }
            catch { /* ignore */ }
        }

        private void AcceptLoop(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    TcpClient client = null;
                    try
                    {
                        client = _listener.AcceptTcpClient();
                        ThreadPool.QueueUserWorkItem(_ => HandleClient(client));
                    }
                    catch (SocketException se)
                    {
                        if (!_running) break; // listener stopped
                        ModLogger.Warn($"Socket exception in accept loop: {se.Message}");
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error($"Accept loop error: {ex}");
            }
        }

        private void HandleClient(TcpClient client)
        {
            using (client)
            using (var stream = client.GetStream())
            using (var reader = new StreamReader(stream, Encoding.ASCII, false, 8192, leaveOpen: true))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 8192, leaveOpen: true) { NewLine = "\r\n", AutoFlush = true })
            {
                try
                {
                    // Parse request line
                    var requestLine = reader.ReadLine();
                    if (string.IsNullOrWhiteSpace(requestLine))
                    {
                        return;
                    }

                    var parts = requestLine.Split(' ');
                    if (parts.Length < 3)
                    {
                        WriteSimpleResponse(writer, 400, "Bad Request", "Invalid request line");
                        return;
                    }

                    string method = parts[0];
                    string target = parts[1];
                    string httpVersion = parts[2];

                    // Parse headers
                    var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    string line;
                    while (!string.IsNullOrEmpty(line = reader.ReadLine()))
                    {
                        int idx = line.IndexOf(':');
                        if (idx > 0)
                        {
                            string key = line.Substring(0, idx).Trim();
                            string value = line.Substring(idx + 1).Trim();
                            headers[key] = value;
                        }
                    }

                    // CORS preflight
                    if (string.Equals(method, "OPTIONS", StringComparison.OrdinalIgnoreCase))
                    {
                        WriteCorsPreflight(writer);
                        return;
                    }

                    // Route API endpoints
                    if (target.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
                    {
                        HandleApi(writer, method, target);
                        return;
                    }

                    if (!string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase) && !string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase))
                    {
                        WriteSimpleResponse(writer, 405, "Method Not Allowed", "Only GET/HEAD/OPTIONS supported");
                        return;
                    }

                    ServeStatic(writer, method, target);
                }
                catch (Exception ex)
                {
                    try { WriteSimpleResponse(writer, 500, "Internal Server Error", ex.Message); } catch { }
                }
            }
        }

        private void HandleApi(StreamWriter writer, string method, string target)
        {
            if (target.Equals("/api/health", StringComparison.OrdinalIgnoreCase))
            {
                var now = DateTime.Now.ToString("o", CultureInfo.InvariantCulture);
                string json = $"{{\"status\":\"ok\",\"port\":{_port},\"time\":\"{now}\"}}";
                WriteJson(writer, 200, json);
                return;
            }

            if (target.Equals("/api/info", StringComparison.OrdinalIgnoreCase))
            {
                string json = "{\"name\":\"Shadows of Doubt Dashboard\",\"version\":\"1.0.0\"}";
                WriteJson(writer, 200, json);
                return;
            }

            // List NPCs
            if (target.Equals("/api/npcs", StringComparison.OrdinalIgnoreCase))
            {
                var list = NpcCache.Snapshot();
                var ordered = list
                    .OrderBy(n => n.name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(n => n.surname ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var sb = new StringBuilder();
                sb.Append("[");
                bool first = true;
                foreach (var npc in ordered)
                {
                    if (!first) sb.Append(",\n");
                    first = false;
                    sb.Append($"{{\"id\":{npc.id},\"name\":\"{JsonEscape(npc.name)}\",\"surname\":\"{JsonEscape(npc.surname)}\",\"photo\":\"{npc.photoBase64 ?? string.Empty}\",\"hpCurrent\":{npc.hpCurrent.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"hpMax\":{npc.hpMax.ToString(System.Globalization.CultureInfo.InvariantCulture)} }}");
                }
                sb.Append("]");
                WriteJson(writer, 200, sb.ToString());
                return;
            }


            WriteSimpleResponse(writer, 404, "Not Found", "Unknown API endpoint");
        }

        private void ServeStatic(StreamWriter writer, string method, string target)
        {
            string path = target;
            int q = path.IndexOf('#');
            if (q >= 0) path = path.Substring(0, q);
            q = path.IndexOf('?');
            if (q >= 0) path = path.Substring(0, q);

            if (path == "/") path = "/index.html";

            // Prevent directory traversal
            string requested = path.Replace('/', Path.DirectorySeparatorChar);
            string full = Path.GetFullPath(Path.Combine(_wwwRoot, requested.TrimStart(Path.DirectorySeparatorChar)));
            string rootFull = Path.GetFullPath(_wwwRoot);
            if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            {
                WriteSimpleResponse(writer, 403, "Forbidden", "Access denied");
                return;
            }

            if (!File.Exists(full))
            {
                WriteSimpleResponse(writer, 404, "Not Found", "File not found");
                return;
            }

            string contentType = GetContentType(Path.GetExtension(full));
            long length = new FileInfo(full).Length;

            WriteStatusLine(writer, 200, "OK");
            WriteCommonHeaders(writer, contentType, length);

            if (!string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase))
            {
                writer.Flush();
                using var fs = File.OpenRead(full);
                fs.CopyTo(writer.BaseStream);
                writer.BaseStream.Flush();
            }
        }

        private static string GetContentType(string ext)
        {
            switch (ext.ToLowerInvariant())
            {
                case ".html": return "text/html; charset=utf-8";
                case ".htm": return "text/html; charset=utf-8";
                case ".css": return "text/css; charset=utf-8";
                case ".js": return "application/javascript; charset=utf-8";
                case ".json": return "application/json; charset=utf-8";
                case ".svg": return "image/svg+xml";
                case ".png": return "image/png";
                case ".jpg":
                case ".jpeg": return "image/jpeg";
                case ".gif": return "image/gif";
                case ".ico": return "image/x-icon";
                case ".woff2": return "font/woff2";
                default: return "application/octet-stream";
            }
        }

        private void WriteCorsPreflight(StreamWriter writer)
        {
            WriteStatusLine(writer, 204, "No Content");
            writer.WriteLine("Access-Control-Allow-Origin: *");
            writer.WriteLine("Access-Control-Allow-Methods: GET, HEAD, OPTIONS");
            writer.WriteLine("Access-Control-Allow-Headers: Content-Type");
            writer.WriteLine("Access-Control-Max-Age: 600");
            writer.WriteLine("Connection: close");
            writer.WriteLine();
        }

        private void WriteJson(StreamWriter writer, int status, string json)
        {
            WriteStatusLine(writer, status, GetReason(status));
            WriteCommonHeaders(writer, "application/json; charset=utf-8", Encoding.UTF8.GetByteCount(json));
            writer.Write(json);
        }

        private void WriteSimpleResponse(StreamWriter writer, int status, string reason, string message)
        {
            string body = $"<html><head><meta charset=\"utf-8\"></head><body><h1>{status} {reason}</h1><p>{WebUtility.HtmlEncode(message)}</p></body></html>";
            WriteStatusLine(writer, status, reason);
            WriteCommonHeaders(writer, "text/html; charset=utf-8", Encoding.UTF8.GetByteCount(body));
            writer.Write(body);
        }

        private void WriteBinaryResponse(StreamWriter writer, string contentType, byte[] data)
        {
            WriteStatusLine(writer, 200, "OK");
            WriteCommonHeaders(writer, contentType, data?.Length ?? 0);
            if (data != null && data.Length > 0)
            {
                writer.Flush();
                writer.BaseStream.Write(data, 0, data.Length);
                writer.BaseStream.Flush();
            }
        }

        private void WriteStatusLine(StreamWriter writer, int status, string reason)
        {
            writer.WriteLine($"HTTP/1.1 {status} {reason}");
        }

        private void WriteCommonHeaders(StreamWriter writer, string contentType, long contentLength)
        {
            writer.WriteLine("Server: SOD-Dashboard");
            writer.WriteLine("Access-Control-Allow-Origin: *");
            writer.WriteLine("Cache-Control: no-cache, no-store, must-revalidate");
            writer.WriteLine("Pragma: no-cache");
            writer.WriteLine("Expires: 0");
            writer.WriteLine($"Content-Type: {contentType}");
            writer.WriteLine($"Content-Length: {contentLength}");
            writer.WriteLine("Connection: close");
            writer.WriteLine();
        }

        private static string GetReason(int status)
        {
            return status switch
            {
                200 => "OK",
                204 => "No Content",
                400 => "Bad Request",
                403 => "Forbidden",
                404 => "Not Found",
                405 => "Method Not Allowed",
                500 => "Internal Server Error",
                _ => "OK"
            };
        }

        private static string JsonEscape(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }

    }
}
