using Duckov;
using Duckov.BlackMarkets;
using Duckov.Buildings;
using Duckov.Economy;
using Duckov.Endowment;
using Duckov.NoteIndexs;
using Duckov.Utilities;
using Duckov.PerkTrees;
using Duckvo.Beacons;
using Saves;
using Object = UnityEngine.Object;

namespace EscapeFromDuckovCoopMod;

internal static class SharedHomeSave
{
    private static ES3File _personal;
    private static ES3File _personalGlobal;
    private static string _path;
    private static readonly Dictionary<string, byte[]> LastValues = new();
    private static readonly Dictionary<string, byte[]> PublishedValues = new();
    internal static bool Capturing { get; private set; }
    internal static bool OverlayActive => _personal != null;
    private static readonly HashSet<string> Keys = new()
    {
        "EXP_Value", "QuestData", "BuildingData", "BeaconManager", "Crafting/UnlockedFormulaIDs",
        "EconomyData", "BlackMarket_Data", "CommonVariablesData", "HasFished", "GamingConsoleData", "BitcoinMiner_Data",
        "MasterKeys", "BaseSceneOverride", "GameRulesManager_RuleIndex", "Rule_Custom", "CustomDifficultyMarker"
    };
    // [max] With personal characters, experience and perks stay in each player's own save.
    internal static bool PersonalKey(string key) => SharedHome.PersonalCharacter && (key == "EXP_Value" || key.StartsWith("PerkTree_"));
    internal static bool SharedKey(string key) => !PersonalKey(key) && (Keys.Contains(key) || key.StartsWith("PerkTree_") ||
        key.StartsWith("PlayerStorage") || key.StartsWith("Inventory/PlayerStorage") || key.StartsWith("Item/Showcase_") || key.StartsWith("StockShop_") ||
        key.StartsWith("ConstructionSite_") || key.StartsWith("NoteIndex") || SharedHomeWorld.ValueKey(key));

    internal static void ResetPublished() => PublishedValues.Clear();

    // [max] Quests the guest finished in their own save. That save is frozen while the overlay is active.
    private static int[] _personalFinishedQuests;
    internal static int[] PersonalFinishedQuests()
    {
        if (_personal == null) return Array.Empty<int>();
        if (_personalFinishedQuests != null) return _personalFinishedQuests;

        var set = new HashSet<int>();
        try
        {
            if (_personal.KeyExists("QuestData") && _personal.Load("QuestData") is Duckov.Quests.QuestManager.SaveData data)
            {
                if (data.completedQuests != null) set.UnionWith(data.completedQuests);
                if (data.historyQuestsData != null)
                    foreach (var entry in data.historyQuestsData)
                        if (entry is Duckov.Quests.Quest.SaveData quest) set.Add(quest.id);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[SharedHome] Could not read personal quests: " + e.Message);
        }

        _personalFinishedQuests = set.ToArray();
        return _personalFinishedQuests;
    }

    // [max] How many of a building the guest owns in their own save: placed in their bunker plus folded tokens.
    private static Dictionary<string, int> _personalOwnedBuildings;
    internal static int PersonalOwnedBuildingCount(string buildingId)
    {
        if (_personal == null || string.IsNullOrEmpty(buildingId)) return 0;
        _personalOwnedBuildings ??= ReadPersonalOwnedBuildings();
        return _personalOwnedBuildings.TryGetValue(buildingId, out var count) ? count : 0;
    }

    private static Dictionary<string, int> ReadPersonalOwnedBuildings()
    {
        var result = new Dictionary<string, int>();
        void Add(string id, int amount)
        {
            if (string.IsNullOrEmpty(id) || amount <= 0) return;
            result[id] = (result.TryGetValue(id, out var current) ? current : 0) + amount;
        }

        try
        {
            if (!_personal.KeyExists("BuildingData")) return result;
            var data = _personal.Load("BuildingData");
            if (data == null) return result;

            var type = data.GetType();
            if (AccessTools.Field(type, "data")?.GetValue(data) is IEnumerable<BuildingManager.BuildingAreaData> areas)
                foreach (var area in areas)
                    if (area?.Buildings != null)
                        foreach (var building in area.Buildings)
                            if (building != null) Add(building.ID, 1);

            if (AccessTools.Field(type, "tokenAmounts")?.GetValue(data) is IEnumerable<BuildingManager.BuildingTokenAmountEntry> tokens)
                foreach (var token in tokens)
                    if (token != null) Add(token.id, token.amount);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[SharedHome] Could not read personal buildings: " + e.Message);
        }

        return result;
    }

    // [max] Crafting recipes the guest unlocked in their own save.
    private static HashSet<string> _personalFormulas;
    internal static bool PersonalFormulaUnlocked(string formulaId)
    {
        if (_personal == null || string.IsNullOrEmpty(formulaId)) return false;
        if (_personalFormulas == null)
        {
            _personalFormulas = new HashSet<string>();
            try
            {
                if (_personal.KeyExists("Crafting/UnlockedFormulaIDs") &&
                    _personal.Load("Crafting/UnlockedFormulaIDs") is IEnumerable<string> ids)
                    _personalFormulas.UnionWith(ids.Where(id => !string.IsNullOrEmpty(id)));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SharedHome] Could not read personal recipes: " + e.Message);
            }
        }

        return _personalFormulas.Contains(formulaId);
    }
    internal static byte[] Capture(bool full = false)
    {
        Capturing = true;
        try
        {
            SavesSystem.CollectSaveData();
            if (!ES3File.cachedFiles.TryGetValue(SavesSystem.CurrentFilePath, out var source))
                throw new InvalidOperationException("Shared home save cache is unavailable");
            var selected = new ES3File(false);
            void Add(string key, object value)
            {
                var single = new ES3File(false);
                single.Save(key, value);
                var bytes = single.LoadRawBytes();
                if (!full && PublishedValues.TryGetValue(key, out var previous) && previous.SequenceEqual(bytes)) return;
                selected.Save(key, value);
                if (!full) PublishedValues[key] = bytes;
            }
            foreach (var key in source.GetKeys().Where(SharedKey))
            {
                // Live warehouse changes already use transactional loot packets.
                if (!full && key.StartsWith("Inventory/PlayerStorage")) continue;
                Add(key, source.Load(key));
            }
            var unlocks = new Dictionary<string, bool>();
            if (EndowmentManager.Instance)
                foreach (var entry in EndowmentManager.Instance.Entries)
                    unlocks[$"Endowment_Unlock_R_{entry.Index}"] = EndowmentManager.GetEndowmentUnlocked(entry.Index);
            unlocks["Difficulty/RageUnlocked"] = SavesSystem.LoadGlobal<bool>("Difficulty/RageUnlocked");
            Add("SharedHomeGlobalUnlocks", unlocks);
            Add("SharedHomeFlagKeys", SharedHomeWorld.FlagKeys);
            if (full) selected.Save("SharedHomeFull", true);
            return selected.GetKeys().Length == 0 ? null : selected.LoadRawBytes();
        }
        finally { Capturing = false; }
    }
    internal static byte[] Combine(byte[] before, byte[] after)
    {
        if (after == null || after.Length == 0) return before;
        if (before == null || before.Length == 0) return after;
        var combined = new ES3File(before);
        var incoming = new ES3File(after);
        foreach (var key in incoming.GetKeys()) combined.Save(key, incoming.Load(key));
        return combined.LoadRawBytes();
    }
    internal static void Begin()
    {
        if (_personal != null) return;
        SavesSystem.KeyExisits("Created");
        // Preserve the current unsaved personal state as well as the disk save.
        if (LevelManager.Instance && LevelManager.LevelInited) LevelManager.Instance.NotifySaveBeforeLoadScene(false);
        else SavesSystem.CollectSaveData();
        _path = SavesSystem.CurrentFilePath;
        _personal = ES3File.cachedFiles[_path];
        ES3File.cachedFiles[_path] = new ES3File(_personal.LoadRawBytes(), _personal.settings);
        if (SharedHome.PersonalCharacter) SharedHomeSafeInventory.KeepPersonal(); // [max] the safe stays the guest's own
        else SharedHomeSafeInventory.ClearPersonal(ES3File.cachedFiles[_path]);
        SavesSystem.LoadGlobal<bool>("SharedHome_CacheProbe");
        if (ES3File.cachedFiles.TryGetValue(SavesSystem.GlobalSaveDataFilePath, out _personalGlobal))
            ES3File.cachedFiles[SavesSystem.GlobalSaveDataFilePath] = new ES3File(_personalGlobal.LoadRawBytes(), _personalGlobal.settings);
    }
    internal static void Apply(byte[] bytes, bool reload = true)
    {
        if (bytes == null || bytes.Length == 0) return;
        Begin();
        var incoming = new ES3File(bytes);
        var target = ES3File.cachedFiles[_path];
        if (incoming.KeyExists("SharedHomeFlagKeys"))
            SharedHomeWorld.AddFlags(incoming.Load<string[]>("SharedHomeFlagKeys"));
        var full = incoming.KeyExists("SharedHomeFull");
        if (full)
            foreach (var key in target.GetKeys().Where(SharedKey)) target.DeleteKey(key);
        var changed = new HashSet<string>();
        foreach (var key in incoming.GetKeys())
        {
            if (key == "SharedHomeFull") continue;
            if (key == "SharedHomeFlagKeys") continue;
            if (key == "SharedHomeGlobalUnlocks")
            {
                var global = ES3File.cachedFiles[SavesSystem.GlobalSaveDataFilePath];
                foreach (var pair in incoming.Load<Dictionary<string, bool>>(key))
                    // [max] Personal characters keep their own endowment unlocks.
                    if (pair.Key.StartsWith("Endowment_Unlock_R_") && !SharedHome.PersonalCharacter || pair.Key == "Difficulty/RageUnlocked") global.Save(pair.Key, pair.Value);
                continue;
            }
            if (PersonalKey(key)) continue; // [max]
            if (!SharedKey(key)) throw new InvalidOperationException("Unexpected shared save key: " + key);
            var value = incoming.Load(key);
            var single = new ES3File(false);
            single.Save(key, value);
            var serialized = single.LoadRawBytes();
            if (!full && LastValues.TryGetValue(key, out var previous) && previous.SequenceEqual(serialized)) continue;
            // Native save types are retained by ES3, including private save-data classes.
            target.Save(key, value);
            LastValues[key] = serialized;
            changed.Add(key);
        }
        if (reload) Reload(full ? null : changed);
    }
    // [max] Personal character: write everything that is not shared (character, safe, level, perks, deaths...)
    // from the session overlay into the guest's own save and store it on disk. Shared keys keep the guest's
    // own values, so their solo bunker, quests, money and storage are untouched.
    private static readonly System.Reflection.FieldInfo CacheField = AccessTools.Field(typeof(ES3File), "cache");
    internal static bool Committing { get; private set; }
    internal static bool CommitPersonal()
    {
        if (_personal == null || !SharedHome.PersonalCharacter || Committing || _path != SavesSystem.CurrentFilePath) return false;
        if (!ES3File.cachedFiles.TryGetValue(_path, out var overlay) || overlay == null || ReferenceEquals(overlay, _personal)) return false;
        try
        {
            bool Personal(string key) => !string.IsNullOrEmpty(key) && !SharedKey(key);
            var keys = new HashSet<string>(overlay.GetKeys().Where(Personal));
            foreach (var key in _personal.GetKeys().Where(k => Personal(k) && !keys.Contains(k)).ToArray())
                _personal.DeleteKey(key);
            var source = CacheField?.GetValue(overlay) as System.Collections.IDictionary;
            var target = CacheField?.GetValue(_personal) as System.Collections.IDictionary;
            foreach (var key in keys)
            {
                // Copy the stored entry as is; fall back to a typed round trip.
                if (source != null && target != null && source.Contains(key)) target[key] = source[key];
                else _personal.Save(key, overlay.Load(key));
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[SharedHome] Personal character could not be prepared for saving: {e}");
            return false;
        }

        Committing = true;
        ES3File.cachedFiles[_path] = _personal;
        try
        {
            SavesSystem.SaveFile();
            CoopSessionLog.Write("SharedHome", "Personal character saved");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[SharedHome] Personal character could not be saved: {e}");
            return false;
        }
        finally
        {
            ES3File.cachedFiles[_path] = overlay;
            Committing = false;
        }
    }
    internal static void Restore()
    {
        if (_personal == null) return;
        ES3File.cachedFiles[_path] = _personal;
        if (_personalGlobal != null) ES3File.cachedFiles[SavesSystem.GlobalSaveDataFilePath] = _personalGlobal;
        _personal = null;
        _personalGlobal = null;
        _personalFinishedQuests = null; // [max]
        _personalOwnedBuildings = null; // [max]
        _personalFormulas = null; // [max]
        LastValues.Clear();
        SharedHome.Reloading = true;
        var defer = SceneLoader.IsSceneLoading;
        if (!defer) SharedHome.Apply(() => Reload(null));
        FinishRestore(defer).Forget();
    }
    private static async UniTaskVoid FinishRestore(bool defer)
    {
        try
        {
            if (defer)
            {
                await UniTask.WaitWhile(() => SceneLoader.IsSceneLoading);
                SharedHome.Apply(() => Reload(null));
            }
            await UniTask.WaitUntil(() => !PlayerStorage.Loading);
        }
        finally { SharedHome.Reloading = false; }
    }
    internal static void Load(object target, string method = "Load")
    {
        if (target == null || target is Object obj && !obj) return;
        var result = AccessTools.Method(target.GetType(), method, Type.EmptyTypes)?.Invoke(target, null);
        if (result is UniTask task) task.Forget();
    }
    private static void Reload(HashSet<string> changed)
    {
        bool Has(string key) => changed == null || changed.Contains(key);
        if (Has("EXP_Value"))
        {
            var previous = EXPManager.EXP;
            var previousLevel = EXPManager.Level;
            Load(EXPManager.Instance);
            if (EXPManager.EXP != previous) EXPManager.onExpChanged?.Invoke(EXPManager.EXP);
            if (EXPManager.Level != previousLevel)
                AccessTools.Method(typeof(EXPManager), "OnLevelChanged").Invoke(null, new object[] { previousLevel, EXPManager.Level });
        }
        if (PerkTreeManager.Instance)
            foreach (var tree in PerkTreeManager.Instance.perkTrees)
                if (tree && Has("PerkTree_" + tree.ID)) SharedHomePerks.Reload(tree);
        if (Has("QuestData")) SharedHomeQuests.Reload();
        if (Has("EconomyData"))
        {
            var previous = EconomyManager.Money;
            Load(EconomyManager.Instance);
            if (EconomyManager.Money != previous)
                (AccessTools.Field(typeof(EconomyManager), "OnMoneyChanged").GetValue(null) as Action<long, long>)?.Invoke(previous, EconomyManager.Money);
        }
        if (Has("Crafting/UnlockedFormulaIDs")) Load(CraftingManager.Instance);
        if (Has("BeaconManager")) Load(BeaconManager.Instance);
        if (Has("NoteIndexData")) Load(NoteIndex.Instance);
        if (Has("MasterKeys")) Load(Duckov.MasterKeys.MasterKeysManager.Instance);
        if (Has("GameRulesManager_RuleIndex") || Has("Rule_Custom"))
        {
            Load(Duckov.Rules.GameRulesManager.Instance, "ReloadCustomRuleSet");
            Duckov.Rules.GameRulesManager.NotifyRuleChanged();
        }
        SharedHomeWorld.Reload(changed);
        if (Has("CommonVariablesData"))
            Load(AccessTools.Field(typeof(CommonVariables), "instance").GetValue(null));
        if (Has("Endowment_SelectedIndex")) Load(EndowmentManager.Instance, "ApplyCurrentEndowment");
        if (Has("BlackMarket_Data")) Load(BlackMarket.Instance);
        if (Has("BuildingData"))
            SharedHomeBuildings.ApplyLayout();
        SharedHomeDisplays.Reload(changed);
        SharedHomeConsoles.Reload(changed);
        SharedHomeMiner.Reload(changed);
        if (changed == null || changed.Any(k => k.StartsWith("PlayerStorage_Buffer")))
        {
            PlayerStorageBuffer.LoadBuffer();
            (AccessTools.Field(typeof(PlayerStorage), "OnTakeBufferItem").GetValue(null) as Action)?.Invoke();
        }
        // Warehouse replicas are updated by the existing authoritative inventory protocol.
        if (changed == null && !SharedHome.Guest) Load(PlayerStorage.Instance);
        if (changed == null && !SharedHome.Guest) SharedHomeLoadouts.ReloadDeaths();
        if (changed == null && !SharedHome.Guest && LevelManager.Instance?.MainCharacter) LevelManager.Instance.RefreshMainCharacterFace();
    }
}
