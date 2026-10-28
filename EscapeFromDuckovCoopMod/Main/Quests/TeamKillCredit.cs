using System.Reflection;
using Duckov.Quests.Tasks;

namespace EscapeFromDuckovCoopMod;

// [max] Team kill credit for quests (independent saves only; in the shared home quests belong to the host).
// When a teammate kills an enemy, the host tells the players who neither killed nor hit it
// (those already get the mod's own kill/assist credit), and each of them counts it for their
// active "kill N enemies" tasks. Enemy type, weapon, headshot and scene rules still apply.
// Tasks that need a buff are skipped (the killer's buffs are unknown here). No XP is granted.
internal static class TeamKillCredit
{
    private static readonly FieldInfo WithWeapon = AccessTools.Field(typeof(QuestTask_KillCount), "withWeapon");
    private static readonly FieldInfo WeaponTypeId = AccessTools.Field(typeof(QuestTask_KillCount), "weaponTypeID");
    private static readonly FieldInfo RequireHeadShot = AccessTools.Field(typeof(QuestTask_KillCount), "requireHeadShot");
    private static readonly FieldInfo WithoutHeadShot = AccessTools.Field(typeof(QuestTask_KillCount), "withoutHeadShot");
    private static readonly FieldInfo RequireBuff = AccessTools.Field(typeof(QuestTask_KillCount), "requireBuff");
    private static readonly FieldInfo RequireEnemyType = AccessTools.Field(typeof(QuestTask_KillCount), "requireEnemyType");
    private static readonly MethodInfo AddCount = AccessTools.Method(typeof(QuestTask_KillCount), "AddCount");

    private static readonly HashSet<int> Credited = new();
    private static bool _initialized;

    private static bool Enabled => CoopAISettings.ActiveGeneral?.TeamQuestKillCredit ?? true;

    internal static void Init()
    {
        if (_initialized) return;
        _initialized = true;
        Health.OnDead += Server_OnDead;
        LevelManager.OnLevelInitialized += Credited.Clear;
    }

    private static void Server_OnDead(Health health, DamageInfo info)
    {
        try
        {
            var service = NetService.Instance;
            if (service == null || !service.networkStarted || !service.IsServer || SharedHome.Active) return;
            if (!health || health.team == Teams.player) return;

            var killer = info.fromCharacter;
            if (!killer) return;
            if (!Credited.Add(health.GetInstanceID())) return;

            NetPeer killerPeer = null;
            if (killer != CharacterMainControl.Main)
            {
                foreach (var kv in service.remoteCharacters)
                {
                    if (kv.Value && kv.Value.GetComponent<CharacterMainControl>() == killer)
                    {
                        killerPeer = kv.Key;
                        break;
                    }
                }

                // Killed by an AI, a trap or anything that is not a player.
                if (killerPeer == null) return;
            }

            var sceneId = string.Empty;
            LocalPlayerManager.Instance?.ComputeIsInGame(out sceneId);

            var victim = health.TryGetCharacter();
            var preset = victim ? victim.characterPreset : null;
            var rpc = new TeamKillCreditRpc
            {
                SceneId = sceneId ?? string.Empty,
                EnemyNameKey = preset ? preset.nameKey : string.Empty,
                WeaponTypeId = info.fromWeaponItemID,
                Crit = info.crit > 0
            };

            // Since 1.4.1 the mod credits players who hit the target (assist), and the killer gets
            // the normal credit. Team credit only goes to everyone else, so no kill counts twice.
            var aiId = TryGetAiId(victim, out var id) ? id : (int?)null;

            var manager = service.netManager;
            if (manager != null)
            {
                foreach (var peer in manager.ConnectedPeerList)
                {
                    if (peer == null || peer == killerPeer) continue;
                    if (aiId.HasValue && HasEvidence(AiQuestCredit.Evidence(aiId.Value, peer))) continue;
                    CoopTool.SendRpcTo(peer, in rpc);
                }
            }

            // The host itself: credited natively only for its own kills and assists (1.4.1+).
            var hostAssisted = aiId.HasValue && HasEvidence(AiQuestCredit.Evidence(aiId.Value, CharacterMainControl.Main));
            if (killerPeer != null && !hostAssisted && Enabled)
                Apply(rpc.EnemyNameKey, rpc.WeaponTypeId, rpc.Crit);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[TeamKillCredit] " + e.Message);
        }
    }

    internal static void Handle(RpcContext context, TeamKillCreditRpc rpc)
    {
        if (context.IsServer || !Enabled || SharedHome.Active) return;

        var lpm = LocalPlayerManager.Instance;
        if (lpm == null || !lpm.ComputeIsInGame(out var sceneId)) return;
        if (!string.IsNullOrEmpty(rpc.SceneId) && !Spectator.AreSameMap(sceneId, rpc.SceneId)) return;

        Apply(rpc.EnemyNameKey, rpc.WeaponTypeId, rpc.Crit);
    }

    private static void Apply(string enemyNameKey, int weaponTypeId, bool crit)
    {
        if (AddCount == null) return;

        foreach (var task in UnityEngine.Object.FindObjectsByType<QuestTask_KillCount>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (!task || !task.isActiveAndEnabled) continue;
            try
            {
                if (Matches(task, enemyNameKey, weaponTypeId, crit))
                    AddCount.Invoke(task, null);
            }
            catch
            {
            }
        }
    }

    private static bool Matches(QuestTask_KillCount task, string enemyNameKey, int weaponTypeId, bool crit)
    {
        if (Get<bool>(RequireBuff, task)) return false;
        if (Get<bool>(WithWeapon, task) && Get<int>(WeaponTypeId, task) != weaponTypeId) return false;
        if (Get<bool>(RequireHeadShot, task) && !crit) return false;
        if (Get<bool>(WithoutHeadShot, task) && crit) return false;
        if (!task.SceneRequirementSatisfied) return false;

        var enemy = RequireEnemyType?.GetValue(task) as CharacterRandomPreset;
        if (enemy && enemy.nameKey != enemyNameKey) return false;

        return true;
    }

    private static bool TryGetAiId(CharacterMainControl victim, out int id)
    {
        id = 0;
        var controller = victim ? victim.aiCharacterController : null;
        if (!controller || !CoopSyncDatabase.AI.TryGet(controller, out var entry) || entry == null) return false;
        id = entry.Id;
        return true;
    }

    private static bool HasEvidence(DamageForwardPayload[] evidence) => evidence != null && evidence.Length > 0;

    private static T Get<T>(FieldInfo field, object target)
    {
        if (field == null) return default;
        return field.GetValue(target) is T value ? value : default;
    }
}
