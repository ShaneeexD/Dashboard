using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Dashboard
{
    // Keep health in sync with the dashboard whenever a citizen takes damage or is healed
    [HarmonyPatch(typeof(Citizen), "RecieveDamage")] // Note: method name as provided (typo in game code)
    internal static class Citizen_ReceiveDamage_Patch
    {
        // Run after the game updates health
        static void Postfix(Citizen __instance)
        {
            var actor = __instance as Actor;
            if (actor != null)
            {
                NpcCache.UpdateHealth(__instance.humanID, actor.currentHealth, actor.maximumHealth);
            }
        }
    }

    // Mark NPC as dead when the game registers a murder
    [HarmonyPatch(typeof(Human), "Murder")]
    internal static class Human_Murder_Patch
    {
        static void Postfix(Human __instance)
        {
            var citizen = __instance as Citizen;
            if (citizen != null)
            {
                NpcCache.UpdateDeath(citizen.humanID, true);
            }
        }
    }

    // KO start/stop events: when KO toggles on, capture total duration; when off, clear
    [HarmonyPatch(typeof(NewAIController), nameof(NewAIController.SetKO))]
    internal static class NewAIController_SetKO_Patch
    {
        static void Postfix(NewAIController __instance, bool val)
        {
            if (__instance == null || __instance.human == null) return;
            var citizen = __instance.human as Citizen;
            if (citizen == null) return;
            if (val)
            {
                float now = SessionData.Instance != null ? SessionData.Instance.gameTime : 0f;
                float total = Math.Max(0f, __instance.koTime - now);
                NpcCache.UpdateKo(citizen.humanID, true, total, total);
            }
            else
            {
                NpcCache.UpdateKo(citizen.humanID, false, 0f, 0f);
            }
        }
    }

    // KO ticking: update remaining seconds while KO is active
    [HarmonyPatch(typeof(NewAIController), "KOUpdate")]
    internal static class NewAIController_KOUpdate_Patch
    {
        static void Postfix(NewAIController __instance)
        {
            if (__instance == null || __instance.human == null) return;
            var citizen = __instance.human as Citizen;
            if (citizen == null) return;
            float now = SessionData.Instance != null ? SessionData.Instance.gameTime : 0f;
            if (__instance.ko)
            {
                float remaining = Math.Max(0f, __instance.koTime - now);
                if (remaining > 0f)
                {
                    NpcCache.UpdateKoTick(citizen.humanID, remaining);
                }
                else
                {
                    // Time is up (or set to 0 by revive) – hide bar immediately
                    NpcCache.UpdateKo(citizen.humanID, false, 0f, 0f);
                }
            }
            else
            {
                // Ensure KO bar resets/hides immediately if KO has ended (e.g., revived by others)
                NpcCache.UpdateKo(citizen.humanID, false, 0f, 0f);
            }
        }
    }

    // Optional: some games have dedicated healing methods; keep in sync as well if present
    [HarmonyPatch(typeof(Actor), "AddHealth")]
    internal static class Actor_Heal_Patch
    {
        static void Postfix(Actor __instance)
        {
            var citizen = __instance as Citizen;
            if (citizen != null)
            {
                NpcCache.UpdateHealth(citizen.humanID, __instance.currentHealth, __instance.maximumHealth);
            }
        }
    }

    [HarmonyPatch(typeof(Actor), "SetHealth")]
    internal static class Actor_Heal_Patch2
    {
        static void Postfix(Actor __instance)
        {
            var citizen = __instance as Citizen;
            if (citizen != null)
            {
                NpcCache.UpdateHealth(citizen.humanID, __instance.currentHealth, __instance.maximumHealth);
            }
        }
    }

    // Runtime in-memory log buffer to capture Game.Log and Game.LogError for the dashboard console
    internal static class RuntimeLogBuffer
    {
        internal struct Entry { public DateTime ts; public string level; public string msg; }
        private static readonly object _lock = new object();
        private static readonly List<Entry> _entries = new List<Entry>(4096);
        private const int MAX = 5000;
        private static string FormatObject(object obj)
        {
            try
            {
                if (obj == null) return "null";
                if (obj is string s) return s;
#if true
                // Handle IL2CPP string/object types gracefully
                try { if (obj is Il2CppSystem.String ils) return ils?.ToString(); } catch { }
                try { if (obj is Il2CppSystem.Object ilo)
                    {
                        // If the object is actually a string boxed as object, ToString() on it will return content
                        var t = ilo.ToString();
                        if (!string.IsNullOrEmpty(t) && !string.Equals(t, nameof(Il2CppSystem.Object), StringComparison.Ordinal)) return t;
                    }
                } catch { }
#endif
                // Fallback to invariant conversion
                return Convert.ToString(obj, System.Globalization.CultureInfo.InvariantCulture) ?? obj.ToString();
            }
            catch { return obj?.ToString() ?? "null"; }
        }

        public static void Add(string level, object obj)
        {
            string msg = FormatObject(obj);
            var e = new Entry { ts = DateTime.UtcNow, level = level ?? "info", msg = msg ?? string.Empty };
            lock(_lock)
            {
                _entries.Add(e);
                if (_entries.Count > MAX)
                {
                    int remove = _entries.Count - MAX;
                    _entries.RemoveRange(0, remove);
                }
            }
        }
        public static List<Entry> Tail(int n)
        {
            lock(_lock)
            {
                int count = _entries.Count;
                int start = Math.Max(0, count - Math.Max(1, n));
                return _entries.GetRange(start, count - start);
            }
        }
        public static int Count { get { lock(_lock) return _entries.Count; } }
    }

    // Subscribe to Unity log so we capture fully-rendered strings as a fallback
    internal static class UnityLogCapture
    {
        private static bool _inited;
        public static void EnsureInit()
        {
            if (_inited) return;
            _inited = true;
            try { Application.logMessageReceived += OnLog; }
            catch { _inited = false; }
        }
        private static void OnLog(string condition, string stackTrace, LogType type)
        {
            string lvl = type == LogType.Error || type == LogType.Exception || type == LogType.Assert ? "error"
                : (type == LogType.Warning ? "warn" : "info");
            RuntimeLogBuffer.Add(lvl, condition);
        }
    }

    // Hook Game.Log(object print, int level = 2)
    [HarmonyPatch(typeof(Game), nameof(Game.Log))]
    internal class Hook_Game_Log
    {
        static void Prefix(object print, int level = 2)
        {
            string lvl = level >= 3 ? "debug" : "info";
            RuntimeLogBuffer.Add(lvl, print);
        }
    }

    // Hook Game.LogError(object print, int level = 2)
    [HarmonyPatch(typeof(Game), nameof(Game.LogError))]
    internal class Hook_Game_LogError
    {
        static void Prefix(object print, int level = 2)
        {
            RuntimeLogBuffer.Add("error", print);
        }
    }
}
