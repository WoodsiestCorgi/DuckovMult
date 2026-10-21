using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

namespace EscapeFromDuckovCoopMod;

// [max] A custom map marker (placed from the map view) was added or removed.
[Rpc(Op.MAP_MARKER, DeliveryMethod.ReliableOrdered, RpcDirection.Bidirectional)]
public struct MapMarkerRpc : IRpcMessage
{
    public bool Remove;
    public string MainSceneId;
    public Vector3 Position;
    public string IconName;
    public string OverrideSceneKey;
    public Color Color;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(Remove);
        writer.Put(MainSceneId ?? string.Empty);
        writer.Put(Position.x);
        writer.Put(Position.y);
        writer.Put(Position.z);
        writer.Put(IconName ?? string.Empty);
        writer.Put(OverrideSceneKey ?? string.Empty);
        writer.Put(Color.r);
        writer.Put(Color.g);
        writer.Put(Color.b);
        writer.Put(Color.a);
    }

    public void Deserialize(NetPacketReader reader)
    {
        Remove = reader.GetBool();
        MainSceneId = reader.GetString();
        Position = new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
        IconName = reader.GetString();
        OverrideSceneKey = reader.GetString();
        Color = new Color(reader.GetFloat(), reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
    }
}
