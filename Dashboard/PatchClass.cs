using BepInEx;
using SOD.Common.BepInEx;
using System.Reflection;
using BepInEx.Configuration;
using System.IO;
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

        public override void Load()
        {
            // Initialize shared logger for the mod (used by LocalHttpServer)
            ModLogger.Initialize(Log);

            Harmony.PatchAll(Assembly.GetExecutingAssembly());
            SaveGameHandlers eventHandler = new SaveGameHandlers();
            Log.LogInfo("Plugin is patched.");

            exampleConfigVariable = Config.Bind("General", "ExampleConfigVariable", false, new ConfigDescription("Example config description."));

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

            if (EnableDashboardServer.Value)
            {
                try
                {
                    var asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                    var wwwRoot = Path.Combine(asmDir ?? ".", "wwwroot");
                    LocalHttpServer.Instance.Start(DashboardPort.Value, wwwRoot);
                }
                catch (System.Exception ex)
                {
                    Log.LogError($"Failed to start dashboard server: {ex}");
                }
            }
        }
    }
}