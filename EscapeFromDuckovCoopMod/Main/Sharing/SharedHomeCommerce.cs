using Duckov.Economy;
using ItemStatsSystem;

namespace EscapeFromDuckovCoopMod;

internal static class SharedHomeCommerce
{
    internal static async UniTaskVoid Buy(string merchant, int type, int amount)
    {
        var shop = SharedHomeWorld.Shops.FirstOrDefault(s => s && s.MerchantID == merchant);
        if (!shop || amount <= 0 || !shop.entries.Any(e => e.ItemTypeID == type && e.Show && e.CurrentStock >= amount)) return;
        await shop.Buy(type, amount);
        SharedHome.MarkDirty();
    }
    // [max] guestUnlocked: the guest has this recipe in their own save.
    internal static async UniTaskVoid Craft(string id, bool guestUnlocked = false)
    {
        if (!CraftingManager.Instance) return;
        if (!CraftingManager.UnlockedFormulaIDs.Contains(id) &&
            !(guestUnlocked && CraftingFormulaCollection.TryGetFormula(id, out _))) return;
        await CraftingManager.Instance.Craft(id);
        SharedHome.MarkDirty();
    }
    internal static void StoreOutput(Item item)
    {
        if (!SharedHome.Active || !NetService.Instance.IsServer || !item) return;
        item.Detach();
        ItemUtilities.SendToPlayerStorage(item);
        SharedHome.MarkDirty();
    }
}
