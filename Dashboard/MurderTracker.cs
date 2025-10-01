using System;
using HarmonyLib;

namespace Dashboard
{
    /// <summary>
    /// Tracks the current murderer and victim from the game's MurderController
    /// </summary>
    public static class MurderTracker
    {
        private static readonly object _lock = new object();
        
        public static int CurrentMurdererId { get; private set; } = -1;
        public static int CurrentVictimId { get; private set; } = -1;
        public static DateTime LastUpdated { get; private set; } = DateTime.MinValue;

        public static void SetMurderer(int humanId)
        {
            lock (_lock)
            {
                CurrentMurdererId = humanId;
                LastUpdated = DateTime.UtcNow;
                ModLogger.Info($"Murder tracker: New murderer set (ID: {humanId})");
            }
        }

        public static void SetVictim(int humanId)
        {
            lock (_lock)
            {
                CurrentVictimId = humanId;
                LastUpdated = DateTime.UtcNow;
                ModLogger.Info($"Murder tracker: New victim set (ID: {humanId})");
            }
        }

        public static (int murdererId, int victimId) GetCurrent()
        {
            lock (_lock)
            {
                return (CurrentMurdererId, CurrentVictimId);
            }
        }
    }

    /// <summary>
    /// Harmony patch for MurderController.PickNewMurderer
    /// </summary>
    [HarmonyPatch(typeof(MurderController), nameof(MurderController.PickNewMurderer))]
    public class MurderController_PickNewMurderer_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(MurderController __instance)
        {
            try
            {
                if (__instance?.currentMurderer != null)
                {
                    MurderTracker.SetMurderer(__instance.currentMurderer.humanID);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error($"MurderController.PickNewMurderer patch error: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Harmony patch for MurderController.PickNewVictim
    /// </summary>
    [HarmonyPatch(typeof(MurderController), nameof(MurderController.PickNewVictim))]
    public class MurderController_PickNewVictim_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(MurderController __instance)
        {
            try
            {
                if (__instance?.currentVictim != null)
                {
                    MurderTracker.SetVictim(__instance.currentVictim.humanID);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error($"MurderController.PickNewVictim patch error: {ex.Message}");
            }
        }
    }
}
