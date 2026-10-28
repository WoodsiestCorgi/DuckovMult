using Duckov;
using Duckov.BlackMarkets;
using Duckov.Economy;
using Duckov.PerkTrees;
using Duckvo.Beacons;

namespace EscapeFromDuckovCoopMod;

internal static class SharedHomeProgress
{
    internal static Teams KillCreditTeam(CharacterMainControl character)
    {
        if (SharedHome.Active && NetService.Instance.IsServer &&
            NetService.Instance.remoteCharacters.Values.Any(go => go && go.GetComponent<CharacterMainControl>() == character)) return Teams.player;
        return character.Team;
    }
    internal static void Init()
    {
        EXPManager.onExpChanged += _ => SharedHome.MarkDirty();
        EconomyManager.OnMoneyChanged += (_, _) => SharedHome.MarkDirty();
        EconomyManager.OnItemUnlockStateChanged += _ => SharedHome.MarkDirty();
        Perk.OnPerkUnlockConfirmed += _ => SharedHome.MarkDirty();
        CraftingManager.OnFormulaUnlocked += _ => SharedHome.MarkDirty();
        BeaconManager.OnBeaconUnlocked += (_, _) => SharedHome.MarkDirty();
        BlackMarket.onRefreshChanceChanged += _ => SharedHome.MarkDirty();
        PlayerStorage.OnPlayerStorageChange += (_, _, _) => SharedHome.MarkDirty();
        PlayerStorage.OnTakeBufferItem += SharedHome.MarkDirty;
        PlayerStorage.OnItemAddedToBuffer += _ => SharedHome.MarkDirty();
        CraftingManager.OnItemCrafted += (_, _) => SharedHome.MarkDirty();
        LevelManager.OnLevelInitialized += SharedHome.MarkDirty;
        Health.OnDead += OnDead;
    }
    private static void OnDead(Health victim, DamageInfo damage)
    {
        if (!SharedHome.Active || !NetService.Instance.IsServer || !victim || victim.team == Teams.player) return;
        if (SharedHome.PersonalCharacter) return; // [max] guests earn experience for their own kills themselves
        var attacker = damage.fromCharacter;
        if (!attacker || attacker == CharacterMainControl.Main) return;
        if (!NetService.Instance.remoteCharacters.Values.Any(go => go && go.GetComponent<CharacterMainControl>() == attacker)) return;
        var item = victim.TryGetCharacter()?.CharacterItem;
        if (item) EXPManager.AddExp(item.GetInt("Exp"));
        SharedHome.MarkDirty();
    }

    // [max] Personal characters: quest experience claimed by the host also goes to every guest.
    internal static void ShareQuestExp(int amount)
    {
        var service = NetService.Instance;
        if (amount <= 0 || !SharedHome.Active || !SharedHome.PersonalCharacter || service == null || !service.IsServer || service.netManager == null) return;
        var data = BitConverter.GetBytes(amount);
        foreach (var peer in service.netManager.ConnectedPeerList)
            if (SharedHomeCompatibility.Allows(peer)) SharedHome.SendPrivate(40, data, peer);
    }

    internal static void ReceiveQuestExp(byte[] data)
    {
        if (!SharedHome.Guest || !SharedHome.PersonalCharacter || data == null || data.Length != 4) return;
        var amount = BitConverter.ToInt32(data, 0);
        if (amount > 0 && amount <= 10_000_000) EXPManager.AddExp(amount);
    }
}
