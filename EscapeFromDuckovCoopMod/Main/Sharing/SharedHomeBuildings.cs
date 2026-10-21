using System.Reflection;
using Duckov.Buildings;
using Object = UnityEngine.Object;

namespace EscapeFromDuckovCoopMod;

internal static class SharedHomeBuildings
{
    private static readonly MethodInfo IsQuestFinishedMethod = AccessTools.Method(typeof(Duckov.Quests.QuestManager), "IsQuestFinished");

    internal static void Init() => BuildingManager.OnBuildingListChanged += SharedHome.MarkDirty;
    private static readonly MethodInfo AddTokenMethod = AccessTools.Method(typeof(BuildingManager), "AddToken");
    private static readonly MethodInfo SetTokenAmountMethod = AccessTools.Method(typeof(BuildingManager), "SetTokenAmount");

    internal static bool ValidPlacement(string areaId, string buildingId, int x, int y, int rotation, ICollection<int> guestQuests = null, bool guestOwnsIt = false)
    {
        if (rotation < 0 || rotation > 3 || !BuildingManager.Instance) return false;
        var info = BuildingManager.GetBuildingInfo(buildingId);
        if (!info.Valid) return false;
        // [max] A guest may also place buildings unlocked by quests finished in their own save,
        // and buildings they already own there.
        if (!guestOwnsIt && !(bool)AccessTools.Method(typeof(BuildingInfo), "RequirementsSatisfied").Invoke(info, null) &&
            !SatisfiedWith(info, guestQuests)) return false;
        var area = UnityEngine.Object.FindObjectsByType<BuildingArea>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
            .FirstOrDefault(a => a.AreaID == areaId);
        var point = new Vector2Int(x, y);
        return area && area.IsPlacementWithinRange(info.Dimensions, (BuildingRotation)rotation, point) &&
            !area.AreaData.Collide(info.Dimensions, (BuildingRotation)rotation, point);
    }
    // [max] Guest side: buildings the guest owns in their own save but the shared home does not have yet.
    internal static int GuestExtraOwned(string buildingId)
    {
        if (!SharedHome.Guest || string.IsNullOrEmpty(buildingId)) return 0;
        var owned = SharedHomeSave.PersonalOwnedBuildingCount(buildingId);
        return owned <= 0 ? 0 : Mathf.Max(0, owned - BuildingManager.GetBuildingAmount(buildingId));
    }

    // [max] Host side: place a building for a guest. When the guest owns more of it than the shared home has,
    // a free token is granted for this placement only (removed again if the placement fails).
    internal static void HostPlaceForGuest(string areaId, string buildingId, int x, int y, int rotation, ICollection<int> guestQuests, int guestOwned)
    {
        var manager = BuildingManager.Instance;
        if (!manager) return;

        var guestOwnsIt = guestOwned > BuildingManager.GetBuildingAmount(buildingId);
        if (!ValidPlacement(areaId, buildingId, x, y, rotation, guestQuests, guestOwnsIt)) return;

        var tokensBefore = manager.GetTokenAmount(buildingId);
        var grantedToken = false;
        if (guestOwnsIt && tokensBefore <= 0 && AddTokenMethod != null)
        {
            AddTokenMethod.Invoke(manager, new object[] { buildingId, 1 });
            grantedToken = true;
        }

        var result = AccessTools.Method(typeof(BuildingManager), "BuyAndPlace").Invoke(null,
            new object[] { areaId, buildingId, new Vector2Int(x, y), (BuildingRotation)rotation });

        if (grantedToken && !(result is BuildingBuyAndPlaceResults placed && placed == BuildingBuyAndPlaceResults.Succeed))
            SetTokenAmountMethod?.Invoke(manager, new object[] { buildingId, tokensBefore });
    }

    // [max] Same rule as BuildingInfo.RequirementsSatisfied, but a quest also counts when it is in extraQuests.
    internal static bool SatisfiedWith(BuildingInfo info, ICollection<int> extraQuests)
    {
        if (extraQuests == null || extraQuests.Count == 0) return false;

        if (info.requireBuildings != null)
            foreach (var building in info.requireBuildings)
                if (!BuildingManager.Any(building)) return false;

        if (info.requireQuests != null)
            foreach (var quest in info.requireQuests)
                if (!extraQuests.Contains(quest) && !IsQuestFinished(quest)) return false;

        return true;
    }

    private static bool IsQuestFinished(int id)
    {
        try
        {
            return IsQuestFinishedMethod != null && (bool)IsQuestFinishedMethod.Invoke(null, new object[] { id });
        }
        catch
        {
            return false;
        }
    }

    internal static void ApplyLayout()
    {
        if (!BuildingManager.Instance) return;
        SharedHomeSave.Load(BuildingManager.Instance);
        foreach (var area in Object.FindObjectsByType<BuildingArea>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!area.gameObject.scene.IsValid()) continue;
            var active = (List<Building>)AccessTools.Field(typeof(BuildingArea), "activeBuildings").GetValue(area);
            var desired = area.AreaData.Buildings.ToDictionary(b => b.GUID);
            foreach (var building in active.ToArray())
            {
                if (!building) { active.Remove(building); continue; }
                if (desired.TryGetValue(building.GUID, out var data))
                {
                    building.transform.position = (Vector3)AccessTools.Method(typeof(BuildingManager.BuildingData), "GetTransformPosition").Invoke(data, null);
                    building.transform.localRotation = Quaternion.Euler(0, (int)data.Rotation * 90, 0);
                    desired.Remove(building.GUID);
                    continue;
                }
                building.gameObject.SetActive(false);
                Object.Destroy(building.gameObject);
                active.Remove(building);
            }
            foreach (var data in desired.Values)
                AccessTools.Method(typeof(BuildingArea), "Display").Invoke(area, new object[] { data });
        }
    }
}
