using BepInEx;
using SOD.Common.BepInEx;
using System.Reflection;
using BepInEx.Configuration;
using System.IO;
using System.Diagnostics;
using System;
using System.Collections.Concurrent;
using System.Threading;
namespace Dashboard
{
    [BepInPlugin(PLUGIN_GUID, PLUGIN_NAME, PLUGIN_VERSION)]
    [BepInDependency(SOD.Common.Plugin.PLUGIN_GUID)]
    public class Plugin : PluginController<Plugin>
    {
        public const string PLUGIN_GUID = "ShaneeexD.Dashboard";
        public const string PLUGIN_NAME = "Dashboard";
        public const string PLUGIN_VERSION = "1.0.0";
        public static ConfigEntry<bool> exampleConfigVariable;
        public static ConfigEntry<bool> EnableDashboardServer;
        public static ConfigEntry<int> DashboardPort;
        public static ConfigEntry<bool> OpenBrowserOnStart;

        public override void Load()
        {
            // Initialize shared logger for the mod (used by LocalHttpServer)
            ModLogger.Initialize(Log);

            Harmony.PatchAll(Assembly.GetExecutingAssembly());
            SaveGameHandlers eventHandler = new SaveGameHandlers();
            Log.LogInfo("Plugin is patched.");

            EnableDashboardServer = Config.Bind(
                "Dashboard",
                "EnableServer",
                true,
                new ConfigDescription("Start the local dashboard web server on game launch."));

            DashboardPort = Config.Bind(
                "Dashboard",
                "Port",
                17856,
                new ConfigDescription("Port for the local dashboard web server (http://127.0.0.1:<Port>)."));

            OpenBrowserOnStart = Config.Bind(
                "Dashboard",
                "OpenBrowserOnStart",
                true,
                new ConfigDescription("Open the default browser to the dashboard URL when the server starts."));

            if (EnableDashboardServer.Value)
            {
                try
                {
                    var asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                    var wwwRoot = Path.Combine(asmDir ?? ".", "wwwroot");
                    LocalHttpServer.Instance.Start(DashboardPort.Value, wwwRoot);

                    if (OpenBrowserOnStart.Value)
                    {
                        try
                        {
                            var url = $"http://127.0.0.1:{DashboardPort.Value}/";
                            var psi = new ProcessStartInfo { FileName = url, UseShellExecute = true };
                            Process.Start(psi);
                        }
                        catch (System.Exception ex2)
                        {
                            Log.LogWarning($"Failed to open browser: {ex2.Message}");
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    Log.LogError($"Failed to start dashboard server: {ex}");
                }
            }
        }

        // --- Main-thread dispatcher (IL2CPP-safe, no extra MonoBehaviour) ---
        private static readonly ConcurrentQueue<Action> _mtQueue = new ConcurrentQueue<Action>();
        private static int _mainThreadId;
        private static bool IsMainThread => Thread.CurrentThread.ManagedThreadId == _mainThreadId;

        private void Awake()
        {
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        public static void Enqueue(Action action)
        {
            if (action == null) return;
            if (IsMainThread)
            {
                try { action(); } catch (Exception) { }
            }
            else
            {
                _mtQueue.Enqueue(action);
            }
        }

        public static T RunSync<T>(Func<T> func, int timeoutMs = 3000)
        {
            if (func == null) throw new ArgumentNullException(nameof(func));
            if (IsMainThread) return func();

            var evt = new ManualResetEvent(false);
            T result = default;
            Exception error = null;
            Enqueue(() =>
            {
                try { result = func(); }
                catch (Exception ex) { error = ex; }
                finally { evt.Set(); }
            });
            if (!evt.WaitOne(timeoutMs)) throw new TimeoutException("Plugin.RunSync timed out");
            if (error != null) throw error;
            return result;
        }
    }
}