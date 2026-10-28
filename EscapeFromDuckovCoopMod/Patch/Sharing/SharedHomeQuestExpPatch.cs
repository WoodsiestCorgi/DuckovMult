using Duckov.Quests.Rewards;

namespace EscapeFromDuckovCoopMod;

// [max] Personal characters in the shared mode: quest experience the host claims is shared with the guests.
[HarmonyPatch(typeof(QuestReward_EXP), nameof(QuestReward_EXP.OnClaim))]
internal static class SharedHomeQuestExpPatch
{
    private static void Prefix(QuestReward_EXP __instance, out bool __state) => __state = __instance.Claimed;

    private static void Postfix(QuestReward_EXP __instance, bool __state)
    {
        if (!__state && __instance.Claimed) SharedHomeProgress.ShareQuestExp(__instance.Amount);
    }
}
