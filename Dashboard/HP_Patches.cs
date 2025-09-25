using System;
using HarmonyLib;

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
}
