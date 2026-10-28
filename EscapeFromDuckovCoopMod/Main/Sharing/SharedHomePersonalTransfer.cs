using System.Text;
using Duckov.Economy;
using Duckov.MasterKeys;
using Duckov.UI;
using Newtonsoft.Json.Linq;

namespace EscapeFromDuckovCoopMod;

// [max] Personal character in the shared mode: what the guest brings from their own save into the shared world.
// - Money on the guest's own account is added to the shared account. The host adds it first and confirms;
//   then the guest's own account is emptied and saved, so the money is not counted twice.
// - Keys registered in the guest's own save are registered in the shared key registry as well. A registration
//   is not spent, so the guest keeps it in their own save.
// Repeats on every connection; the same token is resent until the host confirms.
internal static class SharedHomePersonalTransfer
{
    private const long MaxAmount = 1_000_000_000_000L;
    private const int MaxKeys = 4096;
    private static readonly System.Reflection.MethodInfo ActivateKey = AccessTools.Method(typeof(MasterKeysManager), "Activate");
    private static readonly Dictionary<NetPeer, Dictionary<string, int>> Accepted = new();
    private static string _token;
    private static long _amount;
    private static int[] _keys;
    private static bool _done;
    private static float _nextSend;

    private static bool InBase => LevelManager.Instance && LevelManager.LevelInited && !SceneLoader.IsSceneLoading &&
        LevelManager.Instance.IsBaseLevel;

    internal static void Reset()
    {
        _token = null;
        _amount = 0;
        _keys = null;
        _done = false;
        _nextSend = 0;
        Accepted.Clear();
    }

    // Guest: send the request once the character is ready in the bunker.
    internal static void Tick()
    {
        if (_done || !SharedHome.Guest || !SharedHome.PersonalCharacter || !SharedHomeSave.OverlayActive ||
            !SharedHomeLoadouts.CanCommit || Time.unscaledTime < _nextSend || !InBase) return;
        if (_token == null)
        {
            _amount = SharedHomeSave.PersonalMoney();
            if (_amount < 0 || _amount > MaxAmount) _amount = 0;
            _keys = SharedHomeSave.PersonalKeys().Take(MaxKeys).ToArray();
            if (_amount == 0 && _keys.Length == 0)
            {
                _done = true;
                return;
            }
            _token = Guid.NewGuid().ToString("N");
        }
        var sent = SharedHome.Request("personalShare", new { amount = _amount, keys = _keys, token = _token });
        _nextSend = Time.unscaledTime + (sent ? 15f : 5f);
    }

    // Host: apply once per token and confirm every time with the number of newly registered keys.
    internal static void HostApply(JObject args, NetPeer sender)
    {
        if (!SharedHome.PersonalCharacter || sender == null) return;
        var token = (string)args["token"];
        var amount = (long?)args["amount"] ?? 0;
        if (string.IsNullOrEmpty(token) || token.Length > 64 || amount < 0 || amount > MaxAmount) return;
        if (!Accepted.TryGetValue(sender, out var tokens)) Accepted[sender] = tokens = new Dictionary<string, int>();
        if (!tokens.TryGetValue(token, out var added))
        {
            // No confirmation while the account is unavailable; the guest resends later.
            if (amount > 0 && !EconomyManager.Add(amount)) return;
            if (args["keys"] is JArray keys && keys.Count <= MaxKeys && ActivateKey != null)
                foreach (var id in keys.Select(k => (int)k).Distinct())
                {
                    if (MasterKeysManager.IsActive(id)) continue;
                    ActivateKey.Invoke(null, new object[] { id });
                    if (MasterKeysManager.IsActive(id)) added++;
                }
            tokens[token] = added;
            SharedHome.MarkDirty();
            CoopSessionLog.Write("SharedHome", $"Guest brought money={amount}, new keys={added}");
        }
        SharedHome.SendPrivate(41, Encoding.UTF8.GetBytes(token + ":" + added), sender);
    }

    // Guest: the host has applied it, empty our own account and save.
    internal static void ReceiveAck(byte[] data)
    {
        if (_token == null || data == null || data.Length > 128) return;
        var parts = Encoding.UTF8.GetString(data).Split(':');
        if (parts.Length != 2 || parts[0] != _token || !int.TryParse(parts[1], out var keys)) return;
        var amount = _amount;
        _token = null;
        _done = true;
        if (amount > 0)
        {
            SharedHomeSave.TakePersonalMoney(amount);
            try
            {
                if (SharedHomeLoadouts.CanCommit && InBase)
                {
                    LevelManager.Instance.NotifySaveBeforeLoadScene(false);
                    if (SharedHomeSave.CommitPersonal()) SharedHomeLoadouts.Committed();
                }
            }
            catch (Exception e) { Debug.LogError($"[SharedHome] Saving after the money transfer failed: {e}"); }
            NotificationText.Push(CoopLocalization.Get("ui.sharedHome.moneyDeposited", amount.ToString("N0")));
        }
        if (keys > 0) NotificationText.Push(CoopLocalization.Get("ui.sharedHome.keysShared", keys));
        CoopSessionLog.Write("SharedHome", $"Brought to the shared world: money={amount}, new keys={keys}");
    }
}
