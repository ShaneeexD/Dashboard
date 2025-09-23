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
