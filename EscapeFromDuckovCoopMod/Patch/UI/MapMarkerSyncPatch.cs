using Duckov.MiniMaps;

namespace EscapeFromDuckovCoopMod;

// [max] A marker placed by hand from the map view (the RuntimeData overload is used when loading saves).
[HarmonyPatch(typeof(MapMarkerManager), nameof(MapMarkerManager.Request), new[] { typeof(Vector3) })]
internal static class MapMarkerRequestSyncPatch
{
    private static void Postfix() => MapMarkerSync.OnLocalAdded();
}

// [max] A marker removed by clicking it on the map.
[HarmonyPatch(typeof(MapMarkerManager), nameof(MapMarkerManager.Release))]
internal static class MapMarkerReleaseSyncPatch
{
    private static void Prefix(MapMarkerPOI entry) => MapMarkerSync.OnLocalRemoving(entry);
}
