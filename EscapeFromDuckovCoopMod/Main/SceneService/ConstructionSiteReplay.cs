using System.Reflection;

namespace EscapeFromDuckovCoopMod;

internal static class ConstructionSiteReplay
{
    private static readonly FieldInfo WasBuilt = AccessTools.Field(typeof(ConstructionSite), "wasBuilt");
    private static readonly MethodInfo OnBuilt = AccessTools.Method(typeof(ConstructionSite), "OnBuilt");
    private static readonly FieldInfo OriginalCost = AccessTools.Field(typeof(ConstructionSite), "cost");
    private static readonly FieldInfo SiteCostTaker = AccessTools.Field(typeof(ConstructionSite), "costTaker");

    private static ConstructionSite FindSite(CostTaker taker)
    {
        return taker ? taker.GetComponentInParent<ConstructionSite>(true) : null;
    }

    private static ConstructionSite FindFreeSite(CostTaker taker)
    {
        var site = FindSite(taker);
        return site && ((Duckov.Economy.Cost)OriginalCost.GetValue(site)).IsFree ? site : null;
    }

    internal static bool IsFreeConstructionCost(CostTaker taker) => FindFreeSite(taker);

    // [max] Bridges and other construction sites are shared whatever they cost:
    // the builder pays locally, every other player gets the site built for free.
    internal static bool IsConstructionSite(CostTaker taker) => FindSite(taker);

    internal static bool TryApply(CostTaker taker)
    {
        var site = FindSite(taker);
        if (!site) return false;
        if (!(bool)WasBuilt.GetValue(site)) OnBuilt.Invoke(site, new object[] { taker });
        return true;
    }

    // [max] Sites already built in the host's world, so late joiners and independent saves catch up.
    internal static List<CostTaker> CollectBuiltSiteCostTakers()
    {
        var result = new List<CostTaker>();
        foreach (var site in UnityEngine.Object.FindObjectsByType<ConstructionSite>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!site || !(bool)WasBuilt.GetValue(site)) continue;
            if (SiteCostTaker.GetValue(site) is CostTaker taker && taker) result.Add(taker);
        }

        return result;
    }
}
