using System.Reflection;
using ItemStatsSystem;
using LiteNetLib;

namespace EscapeFromDuckovCoopMod;

// [max] Shares "this container has been fully searched" between all players:
// the interact marker turns grey and every item inside counts as inspected.
internal static class LootSearchedSync
{
    private static readonly FieldInfo LootboxInventoryField =
        AccessTools.Field(typeof(InteractableLootbox), "inventoryReference");

    // Inventories already known as searched on this machine (sent or received), to avoid echo loops.
    private static readonly HashSet<Inventory> Known = new(new RefEq());

    // Host only: what to replay to players that join the raid later.
    private static readonly Dictionary<(int scene, int posKey, int lootUid), LootSearchedRpc> ServerSearched = new();

    [ThreadStatic] private static bool _applying;

    internal static void Reset()
    {
        Known.Clear();
        ServerSearched.Clear();
    }

    // Called on the machine of the player who just finished searching.
    internal static void OnLocalSearched(Inventory inv)
    {
        if (_applying || !inv) return;

        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.networkStarted) return;
        if (!LootboxDetectUtil.IsLootboxInventory(inv)) return;
        if (!Known.Add(inv)) return;

        var lm = LootManager.Instance;
        if (lm == null) return;

        var rpc = new LootSearchedRpc
        {
            Id = lm.BuildLootIdentifier(inv),
            PositionHint = inv.transform.position
        };

        if (mod.IsServer)
            RememberOnServer(in rpc);

        // Server: broadcast to every client. Client: send to the host, who relays it.
        CoopTool.SendRpc(in rpc);
    }

    internal static void Handle(RpcContext context, LootSearchedRpc rpc)
    {
        var lm = LootManager.Instance;
        if (lm == null) return;

        var inv = lm.ResolveLootInv(rpc.Id.Scene, rpc.Id.PositionKey, rpc.Id.InstanceId, rpc.Id.LootUid);
        if (!inv && context.IsServer && rpc.PositionHint != Vector3.zero)
            lm.Server_TryResolveLootAggressive(rpc.Id.Scene, rpc.Id.PositionKey, rpc.Id.InstanceId, rpc.PositionHint, out inv);

        if (context.IsServer)
        {
            var key = (rpc.Id.Scene, rpc.Id.PositionKey, rpc.Id.LootUid);
            if (ServerSearched.ContainsKey(key)) return;

            RememberOnServer(in rpc);
            CoopTool.SendRpc(in rpc, context.Sender);
        }

        if (inv) Apply(inv);
    }

    internal static void Server_ReplayToPeer(NetPeer peer)
    {
        if (peer == null) return;
        foreach (var rpc in ServerSearched.Values)
            CoopTool.SendRpcTo(peer, in rpc);
    }

    private static void RememberOnServer(in LootSearchedRpc rpc)
    {
        ServerSearched[(rpc.Id.Scene, rpc.Id.PositionKey, rpc.Id.LootUid)] = rpc;
    }

    private static void Apply(Inventory inv)
    {
        Known.Add(inv);
        _applying = true;
        try
        {
            try
            {
                inv.hasBeenInspectedInLootBox = true;
            }
            catch
            {
            }

            LootSearchWorldGate.TrySetNeedInspection(inv, false);

            try
            {
                foreach (var item in inv)
                {
                    if (!item) continue;
                    item.Inspected = true;
                    item.Inspecting = false;
                }
            }
            catch
            {
            }

            var lootbox = FindLootbox(inv);
            if (lootbox) lootbox.SetMarkerUsed();
        }
        finally
        {
            _applying = false;
        }
    }

    private static InteractableLootbox FindLootbox(Inventory inv)
    {
        try
        {
            if (CoopSyncDatabase.Loot.TryGetByInventory(inv, out var entry) && entry?.Lootbox)
                return entry.Lootbox;
        }
        catch
        {
        }

        if (LootboxInventoryField == null) return null;

        foreach (var box in UnityEngine.Object.FindObjectsByType<InteractableLootbox>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (box && ReferenceEquals(LootboxInventoryField.GetValue(box), inv))
                return box;
        }

        return null;
    }

    private sealed class RefEq : IEqualityComparer<Inventory>
    {
        public bool Equals(Inventory x, Inventory y) => ReferenceEquals(x, y);
        public int GetHashCode(Inventory obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
