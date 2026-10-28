using System.Text;
using Duckov.Economy;
using Duckov.UI;
using Newtonsoft.Json.Linq;

namespace EscapeFromDuckovCoopMod;

// [max] Personal character in the shared mode: money on the guest's own account is added to the shared
// account. The host adds it first and confirms; then the guest's own account is emptied and saved, so the
// money is not counted twice. Repeats on every connection while the guest's own account has money.
internal static class SharedHomePersonalMoney
{
    private const long MaxAmount = 1_000_000_000_000L;
    private static readonly Dictionary<NetPeer, HashSet<string>> Accepted = new();
    private static string _token;
    private static long _amount;
    private static bool _done;
    private static float _nextSend;

    private static bool InBase => LevelManager.Instance && LevelManager.LevelInited && !SceneLoader.IsSceneLoading &&
        LevelManager.Instance.IsBaseLevel;

    internal static void Reset()
    {
        _token = null;
        _amount = 0;
        _done = false;
        _nextSend = 0;
        Accepted.Clear();
    }

    // Guest: send the request once the character is ready in the bunker; resend the same token until confirmed.
    internal static void Tick()
    {
        if (_done || !SharedHome.Guest || !SharedHome.PersonalCharacter || !SharedHomeSave.OverlayActive ||
            !SharedHomeLoadouts.CanCommit || Time.unscaledTime < _nextSend || !InBase) return;
        if (_token == null)
        {
            _amount = SharedHomeSave.PersonalMoney();
            if (_amount <= 0 || _amount > MaxAmount)
            {
                _done = true;
                return;
            }
            _token = Guid.NewGuid().ToString("N");
        }
        var sent = SharedHome.Request("personalMoney", new { amount = _amount, token = _token });
        _nextSend = Time.unscaledTime + (sent ? 15f : 5f);
    }

    // Host: add once per token and confirm every time.
    internal static void HostDeposit(JObject args, NetPeer sender)
    {
        if (!SharedHome.PersonalCharacter || sender == null) return;
        var token = (string)args["token"];
        var amount = (long?)args["amount"] ?? 0;
        if (string.IsNullOrEmpty(token) || token.Length > 64 || amount <= 0 || amount > MaxAmount) return;
        if (!Accepted.TryGetValue(sender, out var tokens)) Accepted[sender] = tokens = new HashSet<string>();
        if (tokens.Add(token))
        {
            EconomyManager.Add(amount);
            SharedHome.MarkDirty();
            CoopSessionLog.Write("SharedHome", $"Guest money added to the shared account: {amount}");
        }
        SharedHome.SendPrivate(41, Encoding.UTF8.GetBytes(token), sender);
    }

    // Guest: the host has the money now, empty our own account and save.
    internal static void ReceiveAck(byte[] data)
    {
        if (_token == null || data == null || data.Length > 128 || Encoding.UTF8.GetString(data) != _token) return;
        var amount = _amount;
        _token = null;
        _done = true;
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
        CoopSessionLog.Write("SharedHome", $"Own money moved to the shared account: {amount}");
        NotificationText.Push(CoopLocalization.Get("ui.sharedHome.moneyDeposited", amount.ToString("N0")));
    }
}
