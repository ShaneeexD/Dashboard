using System;

namespace Dashboard
{
    public static class GameStateCache
    {
        private static readonly object _lock = new object();

        public static bool Ready { get; private set; }
        public static string SaveName { get; private set; } = "DEFAULT_SAVE";
        public static string MurderMO { get; private set; } = string.Empty;
        public static string CityName { get; private set; } = string.Empty;
        public static string TimeText { get; private set; } = string.Empty;

        public static void SetBaseInfo(string saveName, string murderMo, string cityName)
        {
            lock(_lock)
            {
                SaveName = saveName ?? "DEFAULT_SAVE";
                MurderMO = murderMo ?? string.Empty;
                CityName = cityName ?? string.Empty;
                Ready = true;
            }
        }

        public static void SetTime(string timeText)
        {
            lock(_lock)
            {
                TimeText = timeText ?? string.Empty;
            }
        }

        public static (string save, string mo, string time, string city, bool ready) Snapshot()
        {
            lock(_lock)
            {
                return (SaveName, MurderMO, TimeText, CityName, Ready);
            }
        }
    }
}
