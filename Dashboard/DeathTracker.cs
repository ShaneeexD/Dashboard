using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;

namespace Dashboard
{
    /// <summary>
    /// Tracks NPC deaths with timestamps
    /// </summary>
    public static class DeathTracker
    {
        public class DeathRecord
        {
            public int humanId;
            public string name;
            public DateTime timestamp;
            public float gameTime; // In-game time when death occurred
            public int gameMonth;  // 1-12
            public int gameDay;    // 1-31
            public int gameYear;   // Public facing year
        }

        private static readonly object _lock = new object();
        private static readonly List<DeathRecord> _deaths = new List<DeathRecord>();
        private const int MaxDeaths = 50; // Keep last 50 deaths

        public static void RecordDeath(int humanId, string name)
        {
            lock (_lock)
            {
                float gameTime = 0f;
                int gameMonth = 0, gameDay = 0, gameYear = 0;
                try
                {
                    if (SessionData.Instance != null)
                    {
                        gameTime = SessionData.Instance.gameTime;
                        // monthInt and dateInt appear to be zero-based in game code
                        gameMonth = SessionData.Instance.monthInt + 1;
                        gameDay = SessionData.Instance.dateInt + 1;
                        // Approximate public year from yearInt + publicYear (pattern used in codebase)
                        gameYear = SessionData.Instance.yearInt + SessionData.Instance.publicYear;
                    }
                }
                catch { }

                _deaths.Add(new DeathRecord
                {
                    humanId = humanId,
                    name = name ?? "Unknown",
                    timestamp = DateTime.UtcNow,
                    gameTime = gameTime,
                    gameMonth = gameMonth,
                    gameDay = gameDay,
                    gameYear = gameYear
                });

                // Keep only the most recent deaths
                if (_deaths.Count > MaxDeaths)
                {
                    _deaths.RemoveAt(0);
                }

                // If the deceased was the currently tracked victim, clear it so the UI stops showing an active victim
                try
                {
                    var current = MurderTracker.GetCurrent();
                    if (current.victimId == humanId)
                    {
                        MurderTracker.SetVictim(-1);
                    }
                }
                catch { }

                ModLogger.Info($"Death recorded: {name} (ID: {humanId})");
            }
        }

        public static List<DeathRecord> GetRecentDeaths(int count = 10)
        {
            lock (_lock)
            {
                return _deaths.OrderByDescending(d => d.timestamp).Take(count).ToList();
            }
        }

        public static int GetTotalDeaths()
        {
            lock (_lock)
            {
                return _deaths.Count;
            }
        }
    }
}
