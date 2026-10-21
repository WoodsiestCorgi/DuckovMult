using LiteNetLib;
using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod;

// [max] An enemy was killed by a teammate: count it for the receiver's kill quests.
[Rpc(Op.TEAM_KILL_CREDIT, DeliveryMethod.ReliableOrdered, RpcDirection.ServerToClient)]
public struct TeamKillCreditRpc : IRpcMessage
{
    public string SceneId;
    public string EnemyNameKey;
    public int WeaponTypeId;
    public bool Crit;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(SceneId ?? string.Empty);
        writer.Put(EnemyNameKey ?? string.Empty);
        writer.Put(WeaponTypeId);
        writer.Put(Crit);
    }

    public void Deserialize(NetPacketReader reader)
    {
        SceneId = reader.GetString();
        EnemyNameKey = reader.GetString();
        WeaponTypeId = reader.GetInt();
        Crit = reader.GetBool();
    }
}
