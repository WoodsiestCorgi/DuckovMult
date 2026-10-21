using Duckov.Buildings;

namespace EscapeFromDuckovCoopMod;

// [max] In the shared home a guest sees the host's quests. Also count the quests the guest
// finished in their own save, so buildings unlocked only on the guest's side show up in the build menu.
// Buildings the guest already owns in their own save are always available.
[HarmonyPatch(typeof(BuildingInfo), "RequirementsSatisfied")]
internal static class SharedHomeBuildingUnlockPatch
{
    private static void Postfix(ref BuildingInfo __instance, ref bool __result)
    {
        if (__result || !SharedHome.Guest) return;

        try
        {
            __result = SharedHomeBuildings.GuestExtraOwned(__instance.id) > 0 ||
                       SharedHomeBuildings.SatisfiedWith(__instance, SharedHomeSave.PersonalFinishedQuests());
        }
        catch
        {
        }
    }
}

// [max] Buildings the guest owns in their own save but the shared home does not have yet
// show up as free tokens in the build menu. The host grants the token when placing.
[HarmonyPatch(typeof(BuildingManager), nameof(BuildingManager.GetTokenAmount))]
internal static class SharedHomeBuildingTokenPatch
{
    private static void Postfix(string id, ref int __result)
    {
        if (!SharedHome.Guest) return;

        try
        {
            __result += SharedHomeBuildings.GuestExtraOwned(id);
        }
        catch
        {
        }
    }
}

// [max] Recipes the guest unlocked in their own save are available at the shared workbench.
[HarmonyPatch(typeof(CraftingManager), "IsFormulaUnlocked")]
internal static class SharedHomeFormulaUnlockPatch
{
    private static void Postfix(string value, ref bool __result)
    {
        if (__result || !SharedHome.Guest) return;

        try
        {
            __result = SharedHomeSave.PersonalFormulaUnlocked(value);
        }
        catch
        {
        }
    }
}
