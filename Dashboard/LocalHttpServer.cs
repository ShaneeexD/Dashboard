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
                    sb.Append($"{{\"id\":{npc.id},\"name\":\"{JsonEscape(npc.name)}\",\"surname\":\"{JsonEscape(npc.surname)}\",\"photo\":\"{npc.photoBase64 ?? string.Empty}\",\"hpCurrent\":{npc.hpCurrent.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"hpMax\":{npc.hpMax.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"isDead\":{(npc.isDead ? "true" : "false")},\"isKo\":{(npc.isKo ? "true" : "false")},\"koRemainingSeconds\":{npc.koRemainingSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"koTotalSeconds\":{npc.koTotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"employer\":\"{JsonEscape(npc.employer)}\",\"jobTitle\":\"{JsonEscape(npc.jobTitle)}\",\"salary\":\"{JsonEscape(npc.salary)}\",\"homeAddress\":\"{JsonEscape(npc.homeAddress)}\" }}");
                }
                sb.Append("]");
                WriteJson(writer, 200, sb.ToString());
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
