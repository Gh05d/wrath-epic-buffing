using HarmonyLib;
using Kingmaker.RuleSystem.Rules.Abilities;

namespace BuffIt2TheLimit.Handlers {

    [HarmonyPatch(typeof(RuleCalculateArcaneSpellFailureChance), nameof(RuleCalculateArcaneSpellFailureChance.OnTrigger))]
    internal static class ArcaneSpellFailurePatch {

        private static void Prefix(RuleCalculateArcaneSpellFailureChance __instance) {
            if (BuffExecutor.ArmorBypassActive > 0) {
                __instance.IgnoreArmor = true;
                __instance.IgnoreShield = true;
            }
        }
    }
}
