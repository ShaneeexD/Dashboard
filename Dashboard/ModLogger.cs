using System;
using BepInEx.Logging;

namespace Dashboard
{
    internal static class ModLogger
    {
        private static ManualLogSource _log;

        public static void Initialize(ManualLogSource log)
        {
            _log = log;
        }

        public static void Info(string message)
        {
            if (_log != null) _log.LogInfo(message);
            else Console.WriteLine("[INFO] " + message);
        }

        public static void Warn(string message)
        {
            if (_log != null) _log.LogWarning(message);
            else Console.WriteLine("[WARN] " + message);
        }

        public static void Error(string message)
        {
            if (_log != null) _log.LogError(message);
            else Console.WriteLine("[ERROR] " + message);
        }
    }
}
