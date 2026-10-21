using System.Reflection;
using Duckov.MiniMaps;
using Duckov.Scenes;

namespace EscapeFromDuckovCoopMod;

// [max] Custom map markers placed from the map view are shared with every player on the same map.
// Adding or removing a marker is relayed through the host; the host also replays its markers to late joiners.
internal static class MapMarkerSync
{
    private const float SamePlaceSqr = 0.6f * 0.6f;
    private static readonly FieldInfo PoisField = AccessTools.Field(typeof(MapMarkerManager), "pois");

    [ThreadStatic] private static bool _applying;

    internal static void OnLocalAdded()
    {
        if (_applying || !NetworkReady()) return;

        var list = Pois();
        if (list == null || list.Count == 0) return;

        var poi = list[list.Count - 1];
        if (poi) Send(false, poi.Data);
    }

    internal static void OnLocalRemoving(MapMarkerPOI entry)
    {
        if (_applying || !entry || !NetworkReady()) return;
        Send(true, entry.Data);
    }

    internal static void Handle(RpcContext context, MapMarkerRpc rpc)
    {
        if (context.IsServer)
            CoopTool.SendRpc(in rpc, context.Sender);

        Apply(in rpc);
    }

    internal static void Server_ReplayToPeer(NetPeer peer)
    {
        if (peer == null) return;

        var list = Pois();
        if (list == null) return;

        var scene = SafeMainSceneId();
        foreach (var poi in list)
        {
            if (!poi) continue;
            var rpc = Build(false, poi.Data, scene);
            CoopTool.SendRpcTo(peer, in rpc);
        }
    }

    private static void Apply(in MapMarkerRpc rpc)
    {
        try
        {
            if (MapMarkerManager.Instance == null) return;
            if (!string.IsNullOrEmpty(rpc.MainSceneId) && rpc.MainSceneId != SafeMainSceneId()) return;

            var existing = Find(rpc.Position, rpc.IconName);
            _applying = true;
            try
            {
                if (rpc.Remove)
                {
                    if (existing) MapMarkerManager.Release(existing);
                }
                else if (!existing)
                {
                    MapMarkerManager.Request(new MapMarkerPOI.RuntimeData
                    {
                        worldPosition = rpc.Position,
                        iconName = rpc.IconName,
                        overrideSceneKey = rpc.OverrideSceneKey,
                        color = rpc.Color
                    });
                }
            }
            finally
            {
                _applying = false;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[MapMarkerSync] " + e.Message);
        }
    }

    private static void Send(bool remove, MapMarkerPOI.RuntimeData data)
    {
        var rpc = Build(remove, data, SafeMainSceneId());
        CoopTool.SendRpc(in rpc);
    }

    private static MapMarkerRpc Build(bool remove, MapMarkerPOI.RuntimeData data, string mainSceneId)
    {
        return new MapMarkerRpc
        {
            Remove = remove,
            MainSceneId = mainSceneId,
            Position = data.worldPosition,
            IconName = data.iconName,
            OverrideSceneKey = data.overrideSceneKey,
            Color = data.color
        };
    }

    private static MapMarkerPOI Find(Vector3 position, string iconName)
    {
        var list = Pois();
        if (list == null) return null;

        foreach (var poi in list)
        {
            if (!poi) continue;
            var data = poi.Data;
            if (!string.Equals(data.iconName ?? string.Empty, iconName ?? string.Empty, StringComparison.Ordinal)) continue;
            if ((data.worldPosition - position).sqrMagnitude <= SamePlaceSqr) return poi;
        }

        return null;
    }

    private static List<MapMarkerPOI> Pois()
    {
        var manager = MapMarkerManager.Instance;
        if (manager == null || PoisField == null) return null;
        return PoisField.GetValue(manager) as List<MapMarkerPOI>;
    }

    private static bool NetworkReady()
    {
        var service = NetService.Instance;
        return service != null && service.networkStarted;
    }

    private static string SafeMainSceneId()
    {
        try
        {
            return MultiSceneCore.MainSceneID ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
