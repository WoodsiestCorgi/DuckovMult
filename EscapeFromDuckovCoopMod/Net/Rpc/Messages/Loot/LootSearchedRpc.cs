using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

namespace EscapeFromDuckovCoopMod;

// [max] A container was fully searched by someone: everyone marks it as used and reveals its items.
[Rpc(Op.LOOT_SEARCHED, DeliveryMethod.ReliableOrdered, RpcDirection.Bidirectional)]
public struct LootSearchedRpc : IRpcMessage
{
    public LootIdentifier Id;
    public Vector3 PositionHint;

    public void Serialize(NetDataWriter writer)
    {
        Id.Serialize(writer);
        writer.PutV3cm(PositionHint);
    }

    public void Deserialize(NetPacketReader reader)
    {
        Id.Deserialize(reader);
        PositionHint = reader.GetV3cm();
    }
}
