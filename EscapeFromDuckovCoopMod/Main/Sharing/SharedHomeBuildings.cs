using System.Reflection;
using Duckov.Buildings;
using Object = UnityEngine.Object;

namespace EscapeFromDuckovCoopMod;

internal static class SharedHomeBuildings
{
    private static readonly MethodInfo IsQuestFinishedMethod = AccessTools.Method(typeof(Duckov.Quests.QuestManager), "IsQuestFinished");

    internal static void Init() => BuildingManager.OnBuildingListChanged += SharedHome.MarkDirty;
    internal static bool ValidPlacement(string areaId, string buildingId, int x, int y, int rotation, ICollection<int> guestQuests = null)
    {
        if (rotation < 0 || rotation > 3 || !BuildingManager.Instance) return false;
        var info = BuildingManager.GetBuildingInfo(buildingId);
        if (!info.Valid) return false;
        // [max] A guest may also place buildings unlocked by quests finished in their own save.
        if (!(bool)AccessTools.Method(typeof(BuildingInfo), "RequirementsSatisfied").Invoke(info, null) &&
            !SatisfiedWith(info, guestQuests)) return false;
        var area = UnityEngine.Object.FindObjectsByType<BuildingArea>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
            .FirstOrDefault(a => a.AreaID == areaId);
        var point = new Vector2Int(x, y);
        return area && area.IsPlacementWithinRange(info.Dimensions, (BuildingRotation)rotation, point) &&
            !area.AreaData.Collide(info.Dimensions, (BuildingRotation)rotation, point);
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
