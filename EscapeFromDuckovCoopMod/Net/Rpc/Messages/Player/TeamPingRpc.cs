using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

namespace EscapeFromDuckovCoopMod;

// [max] A player pinged a spot for the team.
[Rpc(Op.TEAM_PING, DeliveryMethod.ReliableOrdered, RpcDirection.Bidirectional)]
public struct TeamPingRpc : IRpcMessage
{
    public string PlayerId;
    public string PlayerName;
    public string SceneId;
    public string SubSceneId;
    public Vector3 Position;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(PlayerId ?? string.Empty);
        writer.Put(PlayerName ?? string.Empty);
        writer.Put(SceneId ?? string.Empty);
        writer.Put(SubSceneId ?? string.Empty);
        writer.PutV3cm(Position);
    }

    public void Deserialize(NetPacketReader reader)
    {
        PlayerId = reader.GetString();
        PlayerName = reader.GetString();
        SceneId = reader.GetString();
        SubSceneId = reader.GetString();
        Position = reader.GetV3cm();
    }
}
