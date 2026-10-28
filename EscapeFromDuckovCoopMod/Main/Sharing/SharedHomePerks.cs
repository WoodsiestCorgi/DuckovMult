using System.Collections;
using Duckov;
using Duckov.PerkTrees;
using ItemStatsSystem;
using Newtonsoft.Json.Linq;
using Saves;

namespace EscapeFromDuckovCoopMod;

internal static class SharedHomePerks
{
    private static readonly Type LevelBuffType = typeof(Perk).Assembly.GetType("Duckov.PerkTrees.Behaviours.AddBuffOnLevelInitialized");

    // [max] Personal character: perks are the guest's own, but the cost is paid from the shared account,
    // the shared warehouse and the guest's own inventory, so the host takes the payment first.
    private static readonly System.Reflection.FieldInfo UnlockingField = AccessTools.Field(typeof(Perk), "unlocking");
    private static readonly System.Reflection.FieldInfo BeginTimeField = AccessTools.Field(typeof(Perk), "unlockingBeginTimeRaw");
    private static readonly System.Reflection.FieldInfo StateChangedField = AccessTools.Field(typeof(Perk), "onUnlockStateChanged");
    private static readonly System.Reflection.MethodInfo NotifyChanged = AccessTools.Method(typeof(PerkTree), "NotifyChildStateChanged");
    private static readonly HashSet<Perk> Pending = new();

    internal static void RequestUnlock(Perk perk) => RequestUnlockAsync(perk).Forget();

    private static async UniTaskVoid RequestUnlockAsync(Perk perk)
    {
        if (!perk || perk.Unlocked || perk.Unlocking || !perk.Master || !Pending.Add(perk)) return;
        try
        {
            var requirement = perk.Requirement;
            if (requirement != null && EXPManager.Level < requirement.level) return;
            if (requirement != null && !await SharedHomeInventoryTransaction.Request("perkPay", new { tree = perk.Master.ID, perk = perk.name })) return;
            if (!perk || perk.Unlocked || perk.Unlocking) return;
            UnlockingField.SetValue(perk, true);
            BeginTimeField.SetValue(perk, DateTime.UtcNow.ToBinary());
            NotifyChanged?.Invoke(perk.Master, new object[] { perk });
            (StateChangedField?.GetValue(perk) as Delegate)?.DynamicInvoke(perk, perk.Unlocked);
        }
        catch (Exception e) { Debug.LogError($"[SharedHome] Perk unlock failed: {e}"); }
        finally { Pending.Remove(perk); }
    }

    internal static bool ExecutePay(JObject args, Item character, Inventory safe, Func<bool> current)
    {
        if (!current() || !SharedHome.PersonalCharacter) return false;
        var tree = PerkTreeManager.GetPerkTree((string)args["tree"]);
        var perk = tree ? tree.Perks.FirstOrDefault(p => p && p.name == (string)args["perk"]) : null;
        var cost = perk ? perk.Requirement?.cost : null;
        if (!cost.HasValue) return false;
        var price = cost.Value;
        return SharedHomeInventoryTransaction.WithInventories(character, safe, () => price.Pay());
    }
    internal static void Reload(PerkTree tree)
    {
        if (!ES3File.cachedFiles.TryGetValue(SavesSystem.CurrentFilePath, out var cache)) return;
        var key = "PerkTree_" + tree.ID;
        if (!cache.KeyExists(key)) return;
        var data = cache.Load(key);
        var entries = ((IEnumerable)AccessTools.Field(data.GetType(), "entries").GetValue(data)).Cast<object>()
            .ToDictionary(e => (string)AccessTools.Field(e.GetType(), "perkName").GetValue(e));
        foreach (var perk in tree.Perks.Where(p => p))
        {
            if (!entries.TryGetValue(perk.name, out var entry)) continue;
            var type = entry.GetType();
            var unlocked = (bool)AccessTools.Field(type, "unlocked").GetValue(entry);
            var unlocking = (bool)AccessTools.Field(type, "unlocking").GetValue(entry);
            var begin = (long)AccessTools.Field(type, "unlockingBeginTime").GetValue(entry);
            var beginField = AccessTools.Field(typeof(Perk), "unlockingBeginTimeRaw");
            var changed = perk.Unlocked != unlocked || perk.Unlocking != unlocking || (long)beginField.GetValue(perk) != begin;
            var unlockChanged = perk.Unlocked != unlocked;
            AccessTools.Field(typeof(Perk), "unlocking").SetValue(perk, unlocking);
            beginField.SetValue(perk, begin);
            if (unlockChanged || changed && !unlocked)
            {
                if (!unlocked && perk.EnabledInCurrentLevel && LevelBuffType != null)
                    foreach (var effect in perk.GetComponents(LevelBuffType))
                        if (AccessTools.Field(LevelBuffType, "prefab").GetValue(effect) is Duckov.Buffs.Buff prefab && prefab)
                            CharacterMainControl.Main?.RemoveBuff(prefab.ID, true);
                AccessTools.PropertySetter(typeof(Perk), nameof(Perk.Unlocked)).Invoke(perk, new object[] { unlocked });
            }
            if (changed) AccessTools.Method(typeof(PerkTree), "NotifyChildStateChanged").Invoke(tree, new object[] { perk });
        }
    }
}
