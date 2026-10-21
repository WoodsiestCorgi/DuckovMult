using Duckov.Buildings;

namespace EscapeFromDuckovCoopMod;

// [max] In the shared home a guest sees the host's quests. Also count the quests the guest
// finished in their own save, so buildings unlocked only on the guest's side show up in the build menu.
[HarmonyPatch(typeof(BuildingInfo), "RequirementsSatisfied")]
internal static class SharedHomeBuildingUnlockPatch
{
    private static void Postfix(ref BuildingInfo __instance, ref bool __result)
    {
        if (__result || !SharedHome.Guest) return;

        try
        {
            __result = SharedHomeBuildings.SatisfiedWith(__instance, SharedHomeSave.PersonalFinishedQuests());
        }
        catch
        {
        }
    }
}
