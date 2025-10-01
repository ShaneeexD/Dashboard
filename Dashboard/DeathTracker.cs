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
        }

        private static readonly object _lock = new object();
        private static readonly List<DeathRecord> _deaths = new List<DeathRecord>();
        private const int MaxDeaths = 50; // Keep last 50 deaths

        public static void RecordDeath(int humanId, string name)
        {
            lock (_lock)
            {
                float gameTime = 0f;
                try
                {
                    if (SessionData.Instance != null)
                    {
                        gameTime = SessionData.Instance.gameTime;
                    }
                }
                catch { }

                _deaths.Add(new DeathRecord
                {
                    humanId = humanId,
                    name = name ?? "Unknown",
                    timestamp = DateTime.UtcNow,
                    gameTime = gameTime
                });

                // Keep only the most recent deaths
                if (_deaths.Count > MaxDeaths)
                {
                    _deaths.RemoveAt(0);
                }

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
