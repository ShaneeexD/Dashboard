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

            // Map: capture minimap as base64 data URL
            if (target.StartsWith("/api/map/capture64", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
                {
                    WriteSimpleResponse(writer, 405, "Method Not Allowed", "Use GET for this endpoint");
                    return;
                }
                try
                {
                    int ord = -1;
                    int w = 1024, h = 1024;
                    string oStr = GetQueryValue(target, "ord");
                    if (!string.IsNullOrEmpty(oStr) && int.TryParse(oStr, out var tmp)) ord = tmp;
                    if (int.TryParse(GetQueryValue(target, "w"), out var tw)) w = Math.Max(64, Math.Min(4096, tw));
                    if (int.TryParse(GetQueryValue(target, "h"), out var th)) h = Math.Max(64, Math.Min(4096, th));

                    bool mb = string.Equals(GetQueryValue(target, "mb"), "1", StringComparison.OrdinalIgnoreCase);
                    byte[] png = Plugin.RunSync(() => MapCapture.Capture(ord, w, h, mb), 8000);
                    if (png == null || png.Length == 0)
                    {
                        WriteJson(writer, 500, "{\"ok\":false,\"message\":\"Failed to capture minimap\"}");
                    }
                    else
                    {
                        string b64 = Convert.ToBase64String(png);
                        string json = $"{{\"ok\":true,\"ordinal\":{ord},\"w\":{w},\"h\":{h},\"dataUrl\":\"data:image/png;base64,{b64}\"}}";
                        WriteJson(writer, 200, json);
                    }
                }
                catch (Exception ex)
                {
                    WriteSimpleResponse(writer, 500, "Internal Server Error", ex.Message);
                }
                return;
            }

            // Map: capture minimap as PNG, even if UI is closed (uses an offscreen clone)
            if (target.StartsWith("/api/map/capture", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
                {
                    WriteSimpleResponse(writer, 405, "Method Not Allowed", "Use GET for this endpoint");
                    return;
                }
                try
                {
                    int ord = -1;
                    int w = 1024, h = 1024;
                    string oStr = GetQueryValue(target, "ord");
                    if (!string.IsNullOrEmpty(oStr)) int.TryParse(oStr, out ord);
                    string wStr = GetQueryValue(target, "w");
                    string hStr = GetQueryValue(target, "h");
                    if (!string.IsNullOrEmpty(wStr) && int.TryParse(wStr, out var wv)) w = Math.Max(64, Math.Min(4096, wv));
                    if (!string.IsNullOrEmpty(hStr) && int.TryParse(hStr, out var hv)) h = Math.Max(64, Math.Min(4096, hv));

                    bool mb = string.Equals(GetQueryValue(target, "mb"), "1", StringComparison.OrdinalIgnoreCase);
                    byte[] png = Plugin.RunSync(() => MapCapture.Capture(ord, w, h, mb), 8000);
                    if (png == null || png.Length == 0)
                    {
                        WriteSimpleResponse(writer, 500, "Internal Server Error", "Failed to capture minimap");
                    }
                    else
                    {
                        WriteBinaryResponse(writer, "image/png", png);
                    }
                }
                catch (Exception ex)
                {
                    WriteSimpleResponse(writer, 500, "Internal Server Error", ex.Message);
                }
                return;
            }

            // Map: activate a specific MapLayer by index
            if (target.StartsWith("/api/map/activate", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase))
                {
                    WriteSimpleResponse(writer, 405, "Method Not Allowed", "Use POST for this endpoint");
                    return;
                }
                try
                {
                    int index = -1;
                    string iStr = GetQueryValue(target, "index");
                    if (!string.IsNullOrEmpty(iStr)) int.TryParse(iStr, out index);
                    var result = Plugin.RunSync(() =>
                    {
                        Transform content = null;
                        try { content = FindMinimapContent(); } catch { }
                        if (content == null) return (ok:false, json:"{\"ok\":false,\"message\":\"Minimap Content not found\"}");

                        int activeIdx = -1;
                        int count = content.childCount;
                        int mapLayerOrdinal = 0;
                        for (int i = 0; i < count; i++)
                        {
                            var ch = content.GetChild(i);
                            if (ch == null) continue;
                            if (!ch.name.StartsWith("MapLayer", StringComparison.OrdinalIgnoreCase)) continue;
                            bool makeActive = (mapLayerOrdinal == index);
                            ch.gameObject.SetActive(makeActive);
                            if (makeActive) activeIdx = mapLayerOrdinal;
                            mapLayerOrdinal++;
                        }
                        var jsonSb = new StringBuilder();
                        jsonSb.Append("{\"ok\":true,\"activeIndex\":").Append(activeIdx).Append('}');
                        return (ok:true, json:jsonSb.ToString());
                    }, 8000);
                    WriteJson(writer, result.ok ? 200 : 500, result.json);
                }
                catch (Exception ex)
                {
                    WriteSimpleResponse(writer, 500, "Internal Server Error", ex.Message);
                }
                return;
            }

            if (target.Equals("/api/info", StringComparison.OrdinalIgnoreCase))
            {
                string json = "{\"name\":\"Shadows of Doubt Dashboard\",\"version\":\"1.0.0\"}";
                WriteJson(writer, 200, json);
                return;
            }

            // Map: list layers (MapLayer clones) and current active
            if (target.StartsWith("/api/map/layers", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
                {
                    WriteSimpleResponse(writer, 405, "Method Not Allowed", "Use GET for this endpoint");
                    return;
                }
                try
                {
                    var result = Plugin.RunSync(() =>
                    {
                        Transform content = null;
                        try { content = FindMinimapContent(); } catch { }

                        var sb = new StringBuilder();
                        sb.Append('{');
                        if (content == null)
                        {
                            sb.Append("\"ok\":false,\"message\":\"Minimap Content not found\"}");
                            return sb.ToString();
                        }

                        sb.Append("\"ok\":true,\"layers\":[");
                        int activeOrdinal = -1;
                        bool first = true;
                        int count = content.childCount;
                        int ordinal = 0;
                        for (int i = 0; i < count; i++)
                        {
                            var ch = content.GetChild(i);
                            if (ch == null) continue;
                            if (!ch.name.StartsWith("MapLayer", StringComparison.OrdinalIgnoreCase)) continue;
                            if (!first) sb.Append(',');
                            first = false;
                            bool act = ch.gameObject.activeSelf;
                            if (act) activeOrdinal = ordinal;
                            sb.Append('{')
                              .Append("\"index\":").Append(i).Append(',')
                              .Append("\"ordinal\":").Append(ordinal).Append(',')
                              .Append("\"name\":\"").Append(JsonEscape(ch.name)).Append('\"').Append(',')
                              .Append("\"activeSelf\":").Append(act ? "true" : "false").Append(',')
                              .Append("\"activeInHierarchy\":").Append(ch.gameObject.activeInHierarchy ? "true" : "false").Append(',')
                              .Append("\"childCount\":").Append(ch.childCount)
                              .Append('}');
                            ordinal++;
                        }
                        sb.Append("],\"activeOrdinal\":").Append(activeOrdinal).Append('}');
                        return sb.ToString();
                    }, 5000);
                    WriteJson(writer, 200, result);
                }
                catch (Exception ex)
                {
                    WriteSimpleResponse(writer, 500, "Internal Server Error", ex.Message);
                }
                return;
            }

            // Map: return a shallow tree of the Content hierarchy (limited depth)
            if (target.StartsWith("/api/map/tree", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
                {
                    WriteSimpleResponse(writer, 405, "Method Not Allowed", "Use GET for this endpoint");
                    return;
                }
                try
                {
                    int maxDepth = 3;
                    string dStr = GetQueryValue(target, "depth");
                    if (!string.IsNullOrEmpty(dStr) && int.TryParse(dStr, out int dVal)) maxDepth = Math.Max(1, Math.Min(6, dVal));

                    var result = Plugin.RunSync(() =>
                    {
                        Transform content = null;
                        try { content = FindMinimapContent(); } catch { }

                        var sb = new StringBuilder();
                        if (content == null) return "{\"ok\":false,\"message\":\"Minimap Content not found\"}";

                        string SerializeNode(Transform t, int depth)
                        {
                            var nsb = new StringBuilder();
                            nsb.Append('{');
                            nsb.Append("\"name\":\"").Append(JsonEscape(t.name)).Append('\"');
                            nsb.Append(',').Append("\"activeSelf\":").Append(t.gameObject.activeSelf ? "true" : "false");
                            nsb.Append(',').Append("\"activeInHierarchy\":").Append(t.gameObject.activeInHierarchy ? "true" : "false");
                            nsb.Append(',').Append("\"childCount\":").Append(t.childCount);
                            if (depth < maxDepth && t.childCount > 0)
                            {
                                nsb.Append(',').Append("\"children\":[");
                                bool firstC = true;
                                for (int i = 0; i < t.childCount; i++)
                                {
                                    var c = t.GetChild(i);
                                    if (!firstC) nsb.Append(',');
                                    firstC = false;
                                    nsb.Append(SerializeNode(c, depth + 1));
                                }
                                nsb.Append(']');
                            }
                            nsb.Append('}');
                            return nsb.ToString();
                        }

                        sb.Append('{');
                        sb.Append("\"ok\":true,\"root\":").Append(SerializeNode(content, 0));
                        sb.Append('}');
                        return sb.ToString();
                    }, 8000);
                    WriteJson(writer, 200, result);
                }
                catch (Exception ex)
                {
                    WriteSimpleResponse(writer, 500, "Internal Server Error", ex.Message);
                }
                return;
            }

            // Runtime logs captured via Harmony hooks on Game.Log/Game.LogError
            if (target.StartsWith("/api/runtime-logs", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
                {
                    WriteSimpleResponse(writer, 405, "Method Not Allowed", "Use GET for this endpoint");
                    return;
                }
                try
                {
                    int tail = 500;
                    string tStr = GetQueryValue(target, "tail");
                    if (!string.IsNullOrEmpty(tStr) && int.TryParse(tStr, out int tVal)) tail = Math.Max(50, Math.Min(5000, tVal));

                    var items = RuntimeLogBuffer.Tail(tail);
                    var sb = new StringBuilder();
                    sb.Append("{\"source\":\"runtime\",\"count\":");
                    sb.Append(items.Count);
                    sb.Append(",\"entries\":[");
                    for (int i = 0; i < items.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        var e = items[i];
                        sb.Append('{');
                        sb.Append("\"ts\":\""); sb.Append(JsonEscape(e.ts.ToString("o", CultureInfo.InvariantCulture))); sb.Append('\"');
                        sb.Append(',');
                        sb.Append("\"level\":\""); sb.Append(JsonEscape(e.level ?? "info")); sb.Append('\"');
                        sb.Append(',');
                        sb.Append("\"msg\":\""); sb.Append(JsonEscape(e.msg ?? string.Empty)); sb.Append('\"');
                        sb.Append('}');
                    }
                    sb.Append("]}");
                    WriteJson(writer, 200, sb.ToString());
                }
                catch (Exception ex)
                {
                    WriteSimpleResponse(writer, 500, "Internal Server Error", ex.Message);
                }
                return;
            }

            // Logs: tail Unity/BepInEx logs from common locations
            if (target.StartsWith("/api/logs", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
                {
                    WriteSimpleResponse(writer, 405, "Method Not Allowed", "Use GET for this endpoint");
                    return;
                }
                try
                {
                    int tail = 500;
                    string tStr = GetQueryValue(target, "tail");
                    if (!string.IsNullOrEmpty(tStr) && int.TryParse(tStr, out int tVal)) tail = Math.Max(50, Math.Min(5000, tVal));

                    string baseDir = Application.persistentDataPath ?? string.Empty;
                    string gameRoot = null;
                    try { var dataPath = Application.dataPath; if(!string.IsNullOrWhiteSpace(dataPath)) gameRoot = Path.GetDirectoryName(dataPath); } catch { }
                    var candidates = new List<string>();
                    // PersistentDataPath logs (Unity Player)
                    candidates.Add(Path.Combine(baseDir, "Player.log"));
                    candidates.Add(Path.Combine(baseDir, "Player-prev.log"));
                    candidates.Add(Path.Combine(baseDir, "output_log.txt"));
                    candidates.Add(Path.Combine(baseDir, "Game.log"));
                    // Game root (BepInEx typical)
                    if(!string.IsNullOrEmpty(gameRoot))
                    {
                        candidates.Add(Path.Combine(gameRoot, "BepInEx", "LogOutput.log"));
                        candidates.Add(Path.Combine(gameRoot, "output_log.txt"));
                        candidates.Add(Path.Combine(gameRoot, "Player.log"));
                    }
                    // Pick the most recently modified candidate
                    string foundPath = null;
                    long size = 0;
                    DateTime? mtime = null;
                    try
                    {
                        var existing = candidates
                            .Where(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p))
                            .Select(p => new { Path = p, Time = File.GetLastWriteTimeUtc(p), Size = new FileInfo(p).Length })
                            .OrderByDescending(x => x.Time)
                            .ToList();
                        if (existing.Count > 0)
                        {
                            foundPath = existing[0].Path;
                            size = existing[0].Size;
                            mtime = existing[0].Time;
                        }
                    }
                    catch { /* ignore */ }

                    string content = string.Empty;
                    bool exists = !string.IsNullOrEmpty(foundPath);
                    if (exists)
                    {
                        try { content = TailFile(foundPath, tail); }
                        catch (Exception ex) { ModLogger.Warn($"/api/logs read error: {ex.Message}"); }
                    }
                    var source = (foundPath != null && foundPath.IndexOf("BepInEx", StringComparison.OrdinalIgnoreCase) >= 0) ? "bepinex" : "unity";
                    string mtimeStr = mtime.HasValue ? mtime.Value.ToString("o", CultureInfo.InvariantCulture) : "";
                    string json = $"{{\"path\":\"{JsonEscape(foundPath ?? Path.Combine(baseDir, "Player.log"))}\",\"exists\":{(exists ? "true" : "false")},\"source\":\"{source}\",\"mtime\":\"{mtimeStr}\",\"size\":{size},\"content\":\"{JsonEscape(content)}\"}}";
                    WriteJson(writer, 200, json);
                }
                catch (Exception ex)
                {
                    WriteSimpleResponse(writer, 500, "Internal Server Error", ex.Message);
                }
                return;
            }

            // Game info (read from cache only; do not touch Unity APIs on this thread)
            if (target.Equals("/api/game", StringComparison.OrdinalIgnoreCase))
            {
                var snap = GameStateCache.Snapshot();
                string json = $"{{\"saveName\":\"{JsonEscape(snap.save)}\",\"murderMO\":\"{JsonEscape(snap.mo)}\",\"timeText\":\"{JsonEscape(snap.time)}\",\"cityName\":\"{JsonEscape(snap.city)}\",\"ready\":{(snap.ready ? "true" : "false")} }}";
                WriteJson(writer, 200, json);
                return;
            }

            if (target.StartsWith("/api/npc/", StringComparison.OrdinalIgnoreCase))
            {
                HandleNpcAction(writer, method, target);
                return;
            }

            if (target.StartsWith("/api/player/", StringComparison.OrdinalIgnoreCase))
            {
                HandlePlayerAction(writer, method, target);
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
                    sb.Append('{');
                    sb.Append("\"id\":").Append(npc.id).Append(',');
                    sb.Append("\"name\":\"").Append(JsonEscape(npc.name)).Append("\",");
                    sb.Append("\"surname\":\"").Append(JsonEscape(npc.surname)).Append("\",");
                    sb.Append("\"photo\":\"").Append(npc.photoBase64 ?? string.Empty).Append("\",");
                    sb.Append("\"hpCurrent\":").Append(npc.hpCurrent.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(',');
                    sb.Append("\"hpMax\":").Append(npc.hpMax.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(',');
                    sb.Append("\"isDead\":").Append(npc.isDead ? "true" : "false").Append(',');
                    sb.Append("\"isKo\":").Append(npc.isKo ? "true" : "false").Append(',');
                    sb.Append("\"koRemainingSeconds\":").Append(npc.koRemainingSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(',');
                    sb.Append("\"koTotalSeconds\":").Append(npc.koTotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(',');
                    sb.Append("\"employer\":\"").Append(JsonEscape(npc.employer)).Append("\",");
                    sb.Append("\"jobTitle\":\"").Append(JsonEscape(npc.jobTitle)).Append("\",");
                    sb.Append("\"salary\":\"").Append(JsonEscape(npc.salary)).Append("\",");
                    sb.Append("\"workAddressId\":").Append(npc.workAddressId).Append(',');
                    sb.Append("\"homeAddress\":\"").Append(JsonEscape(npc.homeAddress)).Append("\",");
                    sb.Append("\"homeAddressId\":").Append(npc.homeAddressId).Append(',');
                    // Additional profile fields
                    sb.Append("\"ageYears\":").Append(npc.ageYears).Append(',');
                    sb.Append("\"ageGroup\":\"").Append(JsonEscape(npc.ageGroup)).Append("\",");
                    sb.Append("\"gender\":\"").Append(JsonEscape(npc.gender)).Append("\",");
                    sb.Append("\"heightCm\":").Append(npc.heightCm).Append(',');
                    sb.Append("\"heightCategory\":\"").Append(JsonEscape(npc.heightCategory)).Append("\",");
                    sb.Append("\"build\":\"").Append(JsonEscape(npc.build)).Append("\",");
                    sb.Append("\"hairType\":\"").Append(JsonEscape(npc.hairType)).Append("\",");
                    sb.Append("\"hairColor\":\"").Append(JsonEscape(npc.hairColor)).Append("\",");
                    sb.Append("\"eyes\":\"").Append(JsonEscape(npc.eyes)).Append("\",");
                    sb.Append("\"shoeSize\":").Append(npc.shoeSize).Append(',');
                    sb.Append("\"glasses\":").Append(npc.glasses ? "true" : "false").Append(',');
                    sb.Append("\"facialHair\":").Append(npc.facialHair ? "true" : "false").Append(',');
                    sb.Append("\"dateOfBirth\":\"").Append(JsonEscape(npc.dateOfBirth)).Append("\",");
                    sb.Append("\"telephoneNumber\":\"").Append(JsonEscape(npc.telephoneNumber)).Append("\",");
                    sb.Append("\"livesInBuilding\":\"").Append(JsonEscape(npc.livesInBuilding)).Append("\",");
                    sb.Append("\"livesOnFloor\":\"").Append(JsonEscape(npc.livesOnFloor)).Append("\",");
                    sb.Append("\"worksInBuilding\":\"").Append(JsonEscape(npc.worksInBuilding)).Append("\",");
                    sb.Append("\"workHours\":\"").Append(JsonEscape(npc.workHours)).Append("\"");
                    sb.Append('}');
                }
                sb.Append("]");
                WriteJson(writer, 200, sb.ToString());
                return;
            }

            // List Addresses
            if (target.Equals("/api/addresses", StringComparison.OrdinalIgnoreCase))
            {
                var list = AddressCache.Snapshot();
                var sb = new StringBuilder();
                sb.Append("[");
                bool first = true;
                foreach (var addr in list)
                {
                    if (!first) sb.Append(",\n");
                    first = false;
                    sb.Append('{');
                    sb.Append("\"id\":").Append(addr.id).Append(',');
                    sb.Append("\"name\":\"").Append(JsonEscape(addr.name)).Append("\",");
                    sb.Append("\"buildingName\":\"").Append(JsonEscape(addr.buildingName)).Append("\",");
                    sb.Append("\"floor\":\"").Append(JsonEscape(addr.floor)).Append("\",");
                    sb.Append("\"floorNumber\":").Append(addr.floorNumber).Append(',');
                    sb.Append("\"addressPreset\":\"").Append(JsonEscape(addr.addressPreset)).Append("\",");
                    sb.Append("\"isResidence\":").Append(addr.isResidence ? "true" : "false").Append(',');
                    sb.Append("\"residentCount\":").Append(addr.residents?.Count ?? 0).Append(',');
                    sb.Append("\"designStyle\":\"").Append(JsonEscape(addr.designStyle)).Append("\",");
                    sb.Append("\"roomCount\":").Append(addr.roomCount);
                    sb.Append('}');
                }
                sb.Append("]");
                WriteJson(writer, 200, sb.ToString());
                return;
            }

            // Get single Address by ID
            if (target.StartsWith("/api/address/", StringComparison.OrdinalIgnoreCase))
            {
                string idStr = target.Substring("/api/address/".Length);
                if (int.TryParse(idStr, out int id))
                {
                    var addr = AddressCache.GetById(id);
                    if (addr != null)
                    {
                        var sb = new StringBuilder();
                        sb.Append('{');
                        sb.Append("\"id\":").Append(addr.id).Append(',');
                        sb.Append("\"name\":\"").Append(JsonEscape(addr.name)).Append("\",");
                        sb.Append("\"buildingName\":\"").Append(JsonEscape(addr.buildingName)).Append("\",");
                        sb.Append("\"floor\":\"").Append(JsonEscape(addr.floor)).Append("\",");
                        sb.Append("\"floorNumber\":").Append(addr.floorNumber).Append(',');
                        sb.Append("\"addressPreset\":\"").Append(JsonEscape(addr.addressPreset)).Append("\",");
                        sb.Append("\"isResidence\":").Append(addr.isResidence ? "true" : "false").Append(',');
                        sb.Append("\"designStyle\":\"").Append(JsonEscape(addr.designStyle)).Append("\",");
                        sb.Append("\"roomCount\":").Append(addr.roomCount).Append(',');
                        sb.Append("\"residents\":[");
                        bool firstResident = true;
                        foreach (var res in addr.residents)
                        {
                            if (!firstResident) sb.Append(",");
                            firstResident = false;
                            sb.Append('{');
                            sb.Append("\"id\":").Append(res.id).Append(',');
                            sb.Append("\"name\":\"").Append(JsonEscape(res.name)).Append("\",");
                            sb.Append("\"surname\":\"").Append(JsonEscape(res.surname)).Append("\",");
                            sb.Append("\"photo\":\"").Append(res.photoBase64 ?? string.Empty).Append("\",");
                            sb.Append("\"jobTitle\":\"").Append(JsonEscape(res.jobTitle ?? string.Empty)).Append("\"");
                            sb.Append('}');
                        }
                        sb.Append("]}");
                        WriteJson(writer, 200, sb.ToString());
                        return;
                    }
                }
                WriteSimpleResponse(writer, 404, "Not Found", "Address not found");
                return;
            }

            WriteSimpleResponse(writer, 404, "Not Found", "Unknown API endpoint");
        }

        private void HandlePlayerAction(StreamWriter writer, string method, string target)
        {
            // GET /api/player/status -> player health and status metrics
            if (target.StartsWith("/api/player/status", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
                {
                    WriteSimpleResponse(writer, 405, "Method Not Allowed", "Use GET for this endpoint");
                    return;
                }
                try
                {
                    var data = Plugin.RunSync(() =>
                    {
                        var p = Player.Instance;
                        if (p == null) return (ok:false, json:"{\"ok\":false,\"message\":\"Player not available\"}");
                        // Health
                        float hCur = p.currentHealth;
                        float hMax = p.GetCurrentMaxHealth();
                        if (hMax <= 0f) hMax = Mathf.Max(1f, p.maximumHealth);
                        // Core needs (0..1)
                        var hu = Mathf.Clamp01(1f - p.nourishment); // hunger
                        var th = Mathf.Clamp01(1f - p.hydration);   // thirst
                        // Tired status in StatusController scales from energy 0..0.2
                        // amount = 1 - energy/0.2 when energy <= 0.2, else status removed
                        var ti = Mathf.Clamp01((0.2f - p.energy) / 0.2f); // 0 at >=0.2 energy, 1 at 0 energy
                        var en = Mathf.Clamp01(p.energy);           // energy
                        // Extra statuses (0..1)
                        var stinky = Mathf.Clamp01(1f - p.hygiene);
                        var cold = Mathf.Clamp01(1f - p.heat);
                        var wet = Mathf.Clamp01(p.wet);
                        var headache = Mathf.Clamp01(p.headache);
                        var bruised = Mathf.Clamp01(p.bruised);
                        var bleeding = Mathf.Clamp01(p.bleeding);

                        var sb = new StringBuilder();
                        sb.Append('{');
                        sb.Append("\"ok\":true,");
                        sb.Append("\"health\":{")
                          .Append("\"current\":").Append(hCur.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                          .Append("\"max\":").Append(hMax.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('}').Append(',');
                        sb.Append("\"needs\":{")
                          .Append("\"hunger\":").Append(hu.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                          .Append("\"thirst\":").Append(th.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                          .Append("\"tiredness\":").Append(ti.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                          .Append("\"energy\":").Append(en.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('}').Append(',');
                        sb.Append("\"status\":{")
                          .Append("\"stinky\":").Append(stinky.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                          .Append("\"cold\":").Append(cold.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                          .Append("\"wet\":").Append(wet.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                          .Append("\"headache\":").Append(headache.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                          .Append("\"bruised\":").Append(bruised.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                          .Append("\"bleeding\":").Append(bleeding.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('}');
                        sb.Append('}');
                        return (ok:true, json:sb.ToString());
                    }, 5000);
                    if (!data.ok)
                    {
                        WriteSimpleResponse(writer, 500, "Internal Server Error", "Player unavailable");
                    }
                    else
                    {
                        WriteJson(writer, 200, data.json);
                    }
                }
                catch (Exception ex)
                {
                    WriteSimpleResponse(writer, 500, "Internal Server Error", ex.Message);
                }
                return;
            }
            // GET /api/player/presets -> list inventory-capable interactable presets
            if (target.StartsWith("/api/player/presets", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
                {
                    WriteSimpleResponse(writer, 405, "Method Not Allowed", "Use GET for this endpoint");
                    return;
                }
                try
                {
                    var names = Plugin.RunSync(() =>
                    {
                        var list = new List<string>();
                        try
                        {
                            var presets = AssetLoader.Instance.GetAllInteractables();
                            if (presets != null)
                            {
                                foreach (var p in presets)
                                {
                                    if (p == null) continue;
                                    if (p.spawnable) list.Add(p.presetName);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            ModLogger.Warn($"Error enumerating presets: {ex.Message}");
                        }
                        list.Sort(StringComparer.OrdinalIgnoreCase);
                        return list;
                    }, 10000);

                    var sb = new StringBuilder();
                    sb.Append('[');
                    for (int i = 0; i < names.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        sb.Append('"').Append(JsonEscape(names[i])).Append('"');
                    }
                    sb.Append(']');
                    WriteJson(writer, 200, sb.ToString());
                }
                catch (Exception ex)
                {
                    WriteSimpleResponse(writer, 500, "Internal Server Error", ex.Message);
                }
                return;
            }

            // POST /api/player/spawn-item?preset=NAME
            if (target.StartsWith("/api/player/spawn-item", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase))
                {
                    WriteSimpleResponse(writer, 405, "Method Not Allowed", "Use POST for this endpoint");
                    return;
                }
                string presetName = GetQueryValue(target, "preset");
                if (string.IsNullOrWhiteSpace(presetName))
                {
                    WriteSimpleResponse(writer, 400, "Bad Request", "Missing preset parameter");
                    return;
                }

                (bool ok, string message) result;
                try
                {
                    result = Plugin.RunSync(() =>
                    {
                        var player = Player.Instance;
                        if (player == null) return (false, "Player not available");
                        // Find preset by name (case-insensitive) via Addressables cache
                        InteractablePreset match = null;
                        var presets = AssetLoader.Instance.GetAllInteractables();
                        if (presets != null)
                        {
                            foreach (var p in presets)
                            {
                                if (p != null && string.Equals(p.presetName, presetName, StringComparison.OrdinalIgnoreCase)) { match = p; break; }
                            }
                        }
                        if (match == null) return (false, $"Preset not found: {presetName}");

                        var interactable = InteractableCreator.Instance.CreateWorldInteractable(match, player, player, null, player.transform.position, player.transform.eulerAngles, null, null, "");
                        if (interactable == null) return (false, "Failed to spawn interactable");
                        interactable.SetSpawnPositionRelevent(false);
                        bool picked = FirstPersonItemController.Instance.PickUpItem(interactable, false, false, true, true, true);
                        if (picked)
                        {
                            interactable.MarkAsTrash(true, false, 0f);
                            return (true, $"Spawned '{match.presetName}' to inventory.");
                        }
                        // Failed to pick up; delete spawned world object
                        interactable.Delete();
                        return (false, "Failed to add item to inventory (not an inventory item or slot not available)");
                    }, 10000);
                }
                catch (Exception ex)
                {
                    result = (false, ex.Message);
                }

                string json = $"{{\"success\":{(result.ok ? "true" : "false")},\"message\":\"{JsonEscape(result.message)}\"}}";
                WriteJson(writer, result.ok ? 200 : 500, json);
                return;
            }

            // POST /api/player/spawn-default -> call Human.SpawnInventoryItems on player
            if (target.StartsWith("/api/player/spawn-default", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase))
                {
                    WriteSimpleResponse(writer, 405, "Method Not Allowed", "Use POST for this endpoint");
                    return;
                }
                (bool ok, string message) result;
                try
                {
                    result = Plugin.RunSync(() =>
                    {
                        var player = Player.Instance;
                        if (player == null) return (false, "Player not available");
                        var human = player as Human;
                        if (human == null) return (false, "Player is not a Human instance");
                        human.SpawnInventoryItems();
                        return (true, "Spawned default inventory items.");
                    }, 10000);
                }
                catch (Exception ex)
                {
                    result = (false, ex.Message);
                }

                string json = $"{{\"success\":{(result.ok ? "true" : "false")},\"message\":\"{JsonEscape(result.message)}\"}}";
                WriteJson(writer, result.ok ? 200 : 500, json);
                return;
            }

            WriteSimpleResponse(writer, 404, "Not Found", "Unknown player API endpoint");
        }

        private static string GetQueryValue(string target, string key)
        {
            try
            {
                int q = target.IndexOf('?');
                if (q < 0) return null;
                string qs = target.Substring(q + 1);
                var parts = qs.Split('&', StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in parts)
                {
                    var kv = part.Split('=', 2);
                    if (kv.Length == 2 && string.Equals(kv[0], key, StringComparison.OrdinalIgnoreCase))
                    {
                        return WebUtility.UrlDecode(kv[1]);
                    }
                }
            }
            catch { }
            return null;
        }

        private void HandleNpcAction(StreamWriter writer, string method, string target)
        {
            if (!string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase))
            {
                WriteSimpleResponse(writer, 405, "Method Not Allowed", "Use POST for NPC actions");
                return;
            }

            var path = target;
            int qIdx = path.IndexOf('?');
            if (qIdx >= 0) path = path.Substring(0, qIdx);
            qIdx = path.IndexOf('#');
            if (qIdx >= 0) path = path.Substring(0, qIdx);

            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < 4)
            {
                WriteSimpleResponse(writer, 400, "Bad Request", "NPC action requires /api/npc/{id}/{action}");
                return;
            }

            if (!int.TryParse(segments[2], out int npcId))
            {
                WriteSimpleResponse(writer, 400, "Bad Request", "Invalid NPC id");
                return;
            }

            string action = segments[3].ToLowerInvariant();
            switch (action)
            {
                case "teleport-player":
                    HandleTeleportPlayer(writer, npcId);
                    break;
                case "teleport-npc":
                    HandleTeleportNpc(writer, npcId);
                    break;
                default:
                    WriteSimpleResponse(writer, 404, "Not Found", "Unknown NPC action");
                    break;
            }
        }

        private void HandleTeleportPlayer(StreamWriter writer, int npcId)
        {
            (bool ok, string message) result;
            try
            {
                result = Plugin.RunSync(() =>
                {
                    var player = Player.Instance;
                    if (player == null)
                        return (false, "Player not available");

                    if (!NpcCache.TryGetCitizen(npcId, out var citizen) || citizen == null)
                        return (false, "NPC not found");

                    var targetTransform = citizen.transform;
                    if (targetTransform == null)
                        return (false, "NPC transform unavailable");

                    // Compute a safe destination slightly behind the NPC to avoid overlapping colliders
                    Vector3 forward = targetTransform.forward.sqrMagnitude > 0.001f ? targetTransform.forward.normalized : Vector3.forward;
                    Vector3 destPos = targetTransform.position - forward * 0.9f + Vector3.up * 0.05f;

                    // Temporarily disable player movement and CharacterController to allow warp while unpaused
                    var fpc = player.fps; // UnityStandardAssets FirstPersonController
                    var cc = player.charController; // CharacterController
                    bool prevMove = fpc != null ? fpc.enableMovement : true;
                    try
                    {
                        // Disable movement (prevents controller from immediately moving us)
                        player.EnablePlayerMovement(false, true);
                        if (cc != null) cc.enabled = false;

                        // Move the controlling transform (prefer FPC transform if available)
                        if (fpc != null) fpc.transform.position = destPos; else player.transform.position = destPos;
                        // Face same direction as NPC (yaw only)
                        var e = player.transform.eulerAngles; e.y = targetTransform.eulerAngles.y; player.transform.eulerAngles = e;

                        if (cc != null) cc.enabled = true;
                        // Update location so systems recalc nodes/rooms
                        player.UpdateGameLocation(0f);
                    }
                    finally
                    {
                        // Restore previous movement state
                        player.EnablePlayerMovement(prevMove, true);
                    }

                    return (true, $"Player teleported to {citizen.GetCitizenName()}");
                }, 10000);
            }
            catch (Exception ex)
            {
                result = (false, $"Teleport failed: {ex.Message}");
            }

            string json = $"{{\"success\":{(result.ok ? "true" : "false")},\"message\":\"{JsonEscape(result.message)}\"}}";
            WriteJson(writer, result.ok ? 200 : 500, json);
        }

        private void HandleTeleportNpc(StreamWriter writer, int npcId)
        {
            (bool ok, string message) result;
            try
            {
                result = Plugin.RunSync(() =>
                {
                    var player = Player.Instance;
                    if (player == null)
                        return (false, "Player not available");

                    if (!NpcCache.TryGetCitizen(npcId, out var citizen) || citizen == null)
                        return (false, "NPC not found");

                    var npcTransform = citizen.transform;
                    var playerTransform = player.transform;
                    if (npcTransform == null || playerTransform == null)
                        return (false, "Transform unavailable");

                    Vector3 offset = playerTransform.forward.normalized * 0.75f;
                    if (!offset.sqrMagnitude.Equals(0f))
                    {
                        npcTransform.position = playerTransform.position + offset;
                    }
                    else
                    {
                        npcTransform.position = playerTransform.position + new Vector3(0.75f, 0f, 0f);
                    }
                    npcTransform.rotation = playerTransform.rotation;
                    citizen.UpdateGameLocation(0f);
                    return (true, $"NPC teleported to player");
                }, 10000);
            }
            catch (Exception ex)
            {
                result = (false, $"Teleport failed: {ex.Message}");
            }

            string json = $"{{\"success\":{(result.ok ? "true" : "false")},\"message\":\"{JsonEscape(result.message)}\"}}";
            WriteJson(writer, result.ok ? 200 : 500, json);
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
            writer.WriteLine("Access-Control-Allow-Methods: GET, HEAD, OPTIONS, POST");
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

        private static Transform FindChildByPath(Transform root, string path)
        {
            if (root == null || string.IsNullOrEmpty(path)) return null;
            var parts = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            Transform cur = root;
            for (int i = 0; i < parts.Length; i++)
            {
                cur = cur.Find(parts[i]);
                if (cur == null) return null;
            }
            return cur;
        }

        private static Transform FindMinimapContent()
        {
            try
            {
                // Resolve from active GameCanvas root so we can find inactive children via Transform.Find
                var root = GameObject.Find("GameCanvas");
                if (root != null)
                {
                    var t = FindChildByPath(root.transform, "MinimapCanvas/Minimap/Scroll View/Viewport/Content");
                    if (t != null) return t;
                }
                // Fallbacks that only work if these are active
                var go = GameObject.Find("GameCanvas/MinimapCanvas/Minimap/Scroll View/Viewport/Content");
                if (go != null) return go.transform;
                var mini = GameObject.Find("GameCanvas/MinimapCanvas/Minimap");
                if (mini != null)
                {
                    var t2 = FindChildByPath(mini.transform, "Scroll View/Viewport/Content");
                    if (t2 != null) return t2;
                }
                var mini2 = GameObject.Find("Minimap");
                if (mini2 != null)
                {
                    var t3 = FindChildByPath(mini2.transform, "Scroll View/Viewport/Content");
                    if (t3 != null) return t3;
                }
            }
            catch { }
            return null;
        }

        // Helper to render the Unity minimap to a PNG via an offscreen clone/camera
        private static class MapCapture
        {
            private static GameObject _cloneRoot;
            private static Camera _cam;
            private static RenderTexture _rt;
            private static int _w, _h;
            private const int LAYER = 30; // isolated layer for capture

            public static byte[] Capture(int ordinal, int width, int height, bool includeBuildings = true)
            {
                try
                {
                    if (!EnsureSetup(width, height)) return null;
                    // Pick floor layer
                    var content = _cloneRoot.transform.Find("Scroll View/Viewport/Content");
                    if (content != null)
                    {
                        int mapOrd = 0;
                        for (int i = 0; i < content.childCount; i++)
                        {
                            var ch = content.GetChild(i);
                            if (ch == null) continue;
                            if (!ch.name.StartsWith("MapLayer", StringComparison.OrdinalIgnoreCase)) continue;
                            bool active = (ordinal < 0 ? ch.gameObject.activeSelf : mapOrd == ordinal);
                            ch.gameObject.SetActive(active);
                            if (active)
                            {
                                // Ensure buildings/background are visible
                                var bg = ch.Find("Background");
                                if (bg != null) {
                                    bg.gameObject.SetActive(true);
                                    if (includeBuildings) { try { EnsureBackgroundButtonsVisible(bg); } catch { /* best-effort */ } }
                                }
                                // Force-enable all UI graphics under the active layer (covers Base overlays too)
                                try { EnsureAllGraphicsVisible(ch); } catch { }

                                // If we intend to include buildings but detect no building visuals in the clone, try capturing from the original
                                if (includeBuildings)
                                {
                                    try
                                    {
                                        if (!HasAnyBuildingVisuals(bg))
                                        {
                                            var fallback = CaptureFromOriginal(ordinal, width, height, includeBuildings);
                                            if (fallback != null && fallback.Length > 0) return fallback;
                                        }
                                    }
                                    catch { /* ignore and proceed with clone */ }
                                }
                            }
                            mapOrd++;
                        }
                    }

                    // Center the ScrollRect around the middle of the content so buildings are in view
                    try
                    {
                        var sr = _cloneRoot.GetComponentInChildren<UnityEngine.UI.ScrollRect>(true);
                        if (sr != null)
                        {
                            sr.StopMovement();
                            sr.horizontalNormalizedPosition = 0.5f;
                            sr.verticalNormalizedPosition = 0.5f;
                            // Disable inertia so it stays centered during capture
                            sr.inertia = false;
                        }
                    }
                    catch { }

                    var prev = RenderTexture.active;
                    _cam.targetTexture = _rt;
                    RenderTexture.active = _rt;
                    // Ensure layout/graphics updated before render
                    Canvas.ForceUpdateCanvases();
                    GL.Clear(true, true, new Color(0f,0f,0f,0f));
                    _cam.Render();
                    var tex = new Texture2D(_w, _h, TextureFormat.RGBA32, false);
                    tex.ReadPixels(new Rect(0, 0, _w, _h), 0, 0);
                    tex.Apply();
                    var png = tex.EncodeToPNG();
                    UnityEngine.Object.Destroy(tex);
                    RenderTexture.active = prev;
                    return png;
                }
                catch { return null; }
            }

            private static bool EnsureSetup(int width, int height)
            {
                if (_cloneRoot == null || _cam == null || _rt == null || _w != width || _h != height)
                {
                    Cleanup();
                    var src = FindMinimapRoot();
                    if (src == null) return false;
                    _cloneRoot = UnityEngine.Object.Instantiate(src);
                    _cloneRoot.name = "DashboardMapClone";
                    UnityEngine.Object.DontDestroyOnLoad(_cloneRoot);
                    SetLayerRecursive(_cloneRoot, LAYER);

                    var canvas = _cloneRoot.GetComponentInParent<Canvas>();
                    if (canvas == null) canvas = _cloneRoot.AddComponent<Canvas>();
                    canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    canvas.pixelPerfect = true;
                    canvas.overrideSorting = true;
                    canvas.sortingOrder = short.MaxValue; // ensure on top
                    try
                    {
                        canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.TexCoord2 | AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent;
                    }
                    catch { }

                    var camGO = new GameObject("DashboardMapCaptureCam");
                    UnityEngine.Object.DontDestroyOnLoad(camGO);
                    _cam = camGO.AddComponent<Camera>();
                    _cam.orthographic = true; // UI render
                    // Some environments flag clearFlags/backgroundColor as read-only; skip them safely
                    // Default camera state is fine for UI capture with transparent RT
                    _cam.cullingMask = (1 << LAYER);
                    _cam.allowHDR = false;
                    _cam.allowMSAA = false;

                    _w = width; _h = height;
                    _rt = new RenderTexture(_w, _h, 24, RenderTextureFormat.ARGB32);
                    _rt.Create();
                    _cam.targetTexture = _rt;
                    canvas.worldCamera = _cam;

                    // Scale UI to target resolution to reduce surprises
                    try
                    {
                        var scaler = _cloneRoot.GetComponentInParent<UnityEngine.UI.CanvasScaler>();
                        if (scaler == null) scaler = _cloneRoot.AddComponent<UnityEngine.UI.CanvasScaler>();
                        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
                        scaler.referenceResolution = new Vector2(_w, _h);
                        scaler.matchWidthOrHeight = 0.5f;
                    }
                    catch { }

                    _cloneRoot.SetActive(true);
                    Canvas.ForceUpdateCanvases();
                }
                return _cloneRoot != null && _cam != null && _rt != null;
            }

            private static GameObject FindMinimapRoot()
            {
                // Resolve from active GameCanvas, which should exist; Transform.Find can see inactive children
                var root = GameObject.Find("GameCanvas");
                if (root != null)
                {
                    var t = FindChildByPath(root.transform, "MinimapCanvas/Minimap");
                    if (t != null) return t.gameObject;
                }
                // Fallbacks if active
                var go = GameObject.Find("GameCanvas/MinimapCanvas/Minimap");
                if (go == null) go = GameObject.Find("Minimap");
                return go;
            }

            private static void SetLayerRecursive(GameObject go, int layer)
            {
                if (go == null) return;
                go.layer = layer;
                var tr = go.transform;
                for (int i = 0; i < tr.childCount; i++)
                {
                    SetLayerRecursive(tr.GetChild(i).gameObject, layer);
                }
            }

            private static void EnsureBackgroundButtonsVisible(Transform bg)
            {
                if (bg == null) return;
                var stack = new System.Collections.Generic.Stack<Transform>();
                stack.Push(bg);
                while (stack.Count > 0)
                {
                    var t = stack.Pop();
                    // Explicitly enable MapButtonComponent clones (buildings)
                    if (t.name.StartsWith("MapButtonComponent", StringComparison.OrdinalIgnoreCase))
                    {
                        t.gameObject.SetActive(true);
                        // If there is a CanvasGroup, make sure it's visible for capture
                        var cg = t.GetComponent<CanvasGroup>();
                        if (cg != null)
                        {
                            cg.alpha = 1f;
                            cg.interactable = false;
                            cg.blocksRaycasts = false;
                        }
                        // Ensure known child visuals are on
                        var c0 = t.Find("Layer0"); if (c0 != null) c0.gameObject.SetActive(true);
                        var c1 = t.Find("Layer1"); if (c1 != null) c1.gameObject.SetActive(true);
                        var gi = t.Find("GeneratedImage"); if (gi != null) gi.gameObject.SetActive(true);
                        var ti = t.Find("TypeIcon"); if (ti != null) ti.gameObject.SetActive(true);
                        // Enable UI Graphics if they were disabled
                        foreach (var g in t.GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
                        {
                            if (g == null) continue;
                            g.enabled = true;
                            // Max out alpha just in case
                            try { var col = g.color; col.a = 1f; g.color = col; } catch { }
                            g.raycastTarget = false;
                            try { g.canvasRenderer.SetAlpha(1f); } catch { }
                        }
                        // Disable culling on CanvasRenderers so they always draw
                        foreach (var cr in t.GetComponentsInChildren<CanvasRenderer>(true))
                        {
                            if (cr == null) continue;
                            cr.cull = false;
                            try { cr.SetAlpha(1f); } catch { }
                        }
                    }
                    for (int i = 0; i < t.childCount; i++) stack.Push(t.GetChild(i));
                }
            }

            private static void EnsureAllGraphicsVisible(Transform root)
            {
                if (root == null) return;
                // CanvasGroups
                foreach (var cg in root.GetComponentsInChildren<CanvasGroup>(true))
                {
                    if (cg == null) continue;
                    cg.alpha = 1f;
                    cg.interactable = false;
                    cg.blocksRaycasts = false;
                }
                // UI Graphics
                foreach (var g in root.GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
                {
                    if (g == null) continue;
                    g.enabled = true;
                    try { var c = g.color; c.a = 1f; g.color = c; } catch { }
                    g.raycastTarget = false;
                    try { g.canvasRenderer.SetAlpha(1f); } catch { }
                }
                // CanvasRenderer culling
                foreach (var cr in root.GetComponentsInChildren<CanvasRenderer>(true))
                {
                    if (cr == null) continue;
                    cr.cull = false;
                    try { cr.SetAlpha(1f); } catch { }
                }
                // Rebuild layouts to ensure geometry updates
                try
                {
                    var rt = root as RectTransform ?? root.GetComponent<RectTransform>();
                    if (rt != null) UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
                }
                catch { }
            }

            // Detect if any building visuals (Images/RawImages) exist under Background/MapButtonComponent
            private static bool HasAnyBuildingVisuals(Transform background)
            {
                if (background == null) return false;
                bool found = false;
                foreach (Transform t in background)
                {
                    if (!t.name.StartsWith("MapButtonComponent", StringComparison.OrdinalIgnoreCase)) continue;
                    foreach (var img in t.GetComponentsInChildren<UnityEngine.UI.Image>(true))
                    {
                        if (img == null) continue;
                        if (img.sprite != null && img.color.a > 0.01f) { found = true; break; }
                    }
                    if (found) break;
                    foreach (var ri in t.GetComponentsInChildren<UnityEngine.UI.RawImage>(true))
                    {
                        if (ri == null) continue;
                        if (ri.texture != null && ri.color.a > 0.01f) { found = true; break; }
                    }
                    if (found) break;
                }
                return found;
            }

            private class CanvasState
            {
                public Canvas c;
                public RenderMode renderMode;
                public Camera worldCam;
                public int sortingOrder;
                public bool overrideSorting;
                public AdditionalCanvasShaderChannels addChannels;
            }

            private class ScrollRectState
            {
                public UnityEngine.UI.ScrollRect sr;
                public float h;
                public float v;
                public bool inertia;
            }

            private static byte[] CaptureFromOriginal(int ordinal, int width, int height, bool includeBuildings)
            {
                GameObject src = null;
                var saved = new System.Collections.Generic.List<(GameObject go, bool active, int layer)>();
                var savedCanvases = new System.Collections.Generic.List<CanvasState>();
                var savedScroll = new System.Collections.Generic.List<ScrollRectState>();
                var savedParents = new System.Collections.Generic.List<(GameObject go, bool active)>();
                try
                {
                    src = FindMinimapRoot();
                    if (src == null) return null;

                    // Ensure camera/rt exist
                    if (_cam == null || _rt == null) { if (!EnsureSetup(width, height)) return null; }

                    // Snapshot state
                    void Walk(Transform t)
                    {
                        saved.Add((t.gameObject, t.gameObject.activeSelf, t.gameObject.layer));
                        for (int i = 0; i < t.childCount; i++) Walk(t.GetChild(i));
                    }
                    Walk(src.transform);

                    // Save and activate parents up to root so src becomes active in hierarchy
                    var p = src.transform.parent;
                    while (p != null)
                    {
                        savedParents.Add((p.gameObject, p.gameObject.activeSelf));
                        p.gameObject.SetActive(true);
                        p = p.parent;
                    }

                    foreach (var c in src.GetComponentsInChildren<Canvas>(true))
                    {
                        savedCanvases.Add(new CanvasState
                        {
                            c = c,
                            renderMode = c.renderMode,
                            worldCam = c.worldCamera,
                            sortingOrder = c.sortingOrder,
                            overrideSorting = c.overrideSorting,
                            addChannels = c.additionalShaderChannels
                        });
                    }
                    // Also include ancestor canvases (e.g., MinimapCanvas)
                    foreach (var c in src.GetComponentsInParent<Canvas>(true))
                    {
                        if (savedCanvases.Any(s => s.c == c)) continue;
                        savedCanvases.Add(new CanvasState
                        {
                            c = c,
                            renderMode = c.renderMode,
                            worldCam = c.worldCamera,
                            sortingOrder = c.sortingOrder,
                            overrideSorting = c.overrideSorting,
                            addChannels = c.additionalShaderChannels
                        });
                    }
                    foreach (var sr in src.GetComponentsInChildren<UnityEngine.UI.ScrollRect>(true))
                    {
                        savedScroll.Add(new ScrollRectState
                        {
                            sr = sr,
                            h = sr.horizontalNormalizedPosition,
                            v = sr.verticalNormalizedPosition,
                            inertia = sr.inertia
                        });
                    }

                    // Prepare for capture
                    src.SetActive(true);
                    SetLayerRecursive(src, LAYER);
                    foreach (var s in savedCanvases)
                    {
                        s.c.renderMode = RenderMode.ScreenSpaceCamera;
                        s.c.worldCamera = _cam;
                        s.c.overrideSorting = true;
                        s.c.sortingOrder = short.MaxValue;
                        try { s.c.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.TexCoord2 | AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent; } catch { }
                    }

                    // Activate desired floor and ensure visuals
                    var content = src.transform.Find("Scroll View/Viewport/Content");
                    if (content == null)
                    {
                        // Try alternate from GameCanvas root
                        var root = GameObject.Find("GameCanvas");
                        if (root != null) content = FindChildByPath(root.transform, "MinimapCanvas/Minimap/Scroll View/Viewport/Content");
                    }
                    if (content != null)
                    {
                        int mapOrd = 0;
                        for (int i = 0; i < content.childCount; i++)
                        {
                            var ch = content.GetChild(i);
                            if (!ch.name.StartsWith("MapLayer", StringComparison.OrdinalIgnoreCase)) continue;
                            bool makeActive = (ordinal < 0 ? ch.gameObject.activeSelf : mapOrd == ordinal);
                            ch.gameObject.SetActive(makeActive);
                            if (makeActive)
                            {
                                var bg = ch.Find("Background");
                                if (bg != null)
                                {
                                    bg.gameObject.SetActive(true);
                                    if (includeBuildings) { try { EnsureBackgroundButtonsVisible(bg); } catch { } }
                                }
                                try { EnsureAllGraphicsVisible(ch); } catch { }
                            }
                            mapOrd++;
                        }
                    }

                    foreach (var s in savedScroll)
                    {
                        if (s?.sr == null) continue;
                        s.sr.StopMovement();
                        s.sr.horizontalNormalizedPosition = 0.5f;
                        s.sr.verticalNormalizedPosition = 0.5f;
                        s.sr.inertia = false;
                    }

                    Canvas.ForceUpdateCanvases();

                    var prev = RenderTexture.active;
                    _cam.targetTexture = _rt;
                    RenderTexture.active = _rt;
                    GL.Clear(true, true, new Color(0f, 0f, 0f, 0f));
                    _cam.Render();
                    var tex = new Texture2D(_w, _h, TextureFormat.RGBA32, false);
                    tex.ReadPixels(new Rect(0, 0, _w, _h), 0, 0);
                    tex.Apply();
                    var png = tex.EncodeToPNG();
                    UnityEngine.Object.Destroy(tex);
                    RenderTexture.active = prev;
                    return png;
                }
                catch { return null; }
                finally
                {
                    // Restore canvases
                    try
                    {
                        foreach (var s in savedCanvases)
                        {
                            if (s?.c == null) continue;
                            s.c.renderMode = s.renderMode;
                            s.c.worldCamera = s.worldCam;
                            s.c.sortingOrder = s.sortingOrder;
                            s.c.overrideSorting = s.overrideSorting;
                            try { s.c.additionalShaderChannels = s.addChannels; } catch { }
                        }
                    }
                    catch { }
                    // Restore ScrollRects
                    try
                    {
                        foreach (var s in savedScroll)
                        {
                            if (s?.sr == null) continue;
                            s.sr.StopMovement();
                            s.sr.horizontalNormalizedPosition = s.h;
                            s.sr.verticalNormalizedPosition = s.v;
                            s.sr.inertia = s.inertia;
                        }
                    }
                    catch { }
                    // Restore active/layers
                    try
                    {
                        for (int i = 0; i < saved.Count; i++)
                        {
                            var e = saved[i];
                            if (e.go == null) continue;
                            e.go.layer = e.layer;
                            if (e.go.activeSelf != e.active) e.go.SetActive(e.active);
                        }
                    }
                    catch { }
                    // Restore parents' active state
                    try
                    {
                        foreach (var sp in savedParents)
                        {
                            if (sp.go == null) continue;
                            if (sp.go.activeSelf != sp.active) sp.go.SetActive(sp.active);
                        }
                    }
                    catch { }
                }
            }

            private static void Cleanup()
            {
                try
                {
                    if (_rt != null) { _rt.Release(); UnityEngine.Object.Destroy(_rt); _rt = null; }
                    if (_cam != null) { UnityEngine.Object.Destroy(_cam.gameObject); _cam = null; }
                    if (_cloneRoot != null) { UnityEngine.Object.Destroy(_cloneRoot); _cloneRoot = null; }
                }
                catch { }
            }
        }

        private static string TailFile(string filePath, int maxLines)
        {
            const int bufferSize = 4096;
            var enc = Encoding.UTF8;
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            long fileLength = fs.Length;
            if (fileLength == 0) return string.Empty;

            int lineCount = 0;
            long pos = fileLength;
            var chunk = new byte[Math.Min(bufferSize, (int)fileLength)];
            while (pos > 0 && lineCount <= maxLines)
            {
                int toRead = (int)Math.Min(chunk.Length, pos);
                pos -= toRead;
                fs.Position = pos;
                fs.Read(chunk, 0, toRead);
                for (int i = toRead - 1; i >= 0; i--)
                {
                    if (chunk[i] == (byte)'\n')
                    {
                        lineCount++;
                        if (lineCount > maxLines)
                        {
                            pos += i + 1; // start of next line
                            goto READTAIL;
                        }
                    }
                }
            }
        READTAIL:
            fs.Position = Math.Max(0, pos);
            using var reader = new StreamReader(fs, enc, detectEncodingFromByteOrderMarks: true, bufferSize: 8192, leaveOpen: true);
            string rest = reader.ReadToEnd();
            return rest ?? string.Empty;
        }

    }
}
