using SharpMinerals.Blocks;
using SharpMinerals.Blocks.Descriptors;
using SharpMinerals.Components;
using SharpMinerals.Entities;
using SharpMinerals.Entities.Components;
using SharpMinerals.Items;
using SharpMinerals.Network.Buffers;

namespace SharpMinerals.Persistence;

/// <summary>Serializes a <see cref="PlayerState"/> to a self-describing <c>byte[]</c> for disk-backed stores.
/// Items are written by registry NAME (see <see cref="StackCodec"/>), so they resolve back even as ids shift.</summary>
public static class PlayerStateCodec {
    const byte Version = 1;

    public static byte[] Serialize(PlayerState state) {
        using var ms = new MemoryStream();
        var s = new MinecraftStream(ms);
        s.WriteUByte(Version);

        var t = state.Transform;
        s.WriteDouble(t.X); s.WriteDouble(t.Y); s.WriteDouble(t.Z);
        s.WriteFloat(t.Yaw); s.WriteFloat(t.Pitch);

        s.WriteFloat(state.Health.Current); s.WriteFloat(state.Health.Max);

        var inv = state.Inventory;
        s.WriteVarInt(inv.SelectedSlot);
        s.WriteVarInt(inv.Storage.Size);
        for (int i = 0; i < inv.Storage.Size; i++)
            StackCodec.Write(s, inv.Storage[i]);

        return ms.ToArray();
    }

    public static PlayerState Deserialize(byte[] data) {
        using var ms = new MemoryStream(data, writable: false);
        var s = new MinecraftStream(ms);
        if (s.ReadUByte() is var version && version != Version)
            throw new NotSupportedException($"Unknown PlayerState format version {version}.");

        var transform = new TransformEntityComponent(s.ReadDouble(), s.ReadDouble(), s.ReadDouble(), s.ReadFloat(), s.ReadFloat());
        var health = new HealthEntityComponent(s.ReadFloat(), s.ReadFloat());

        int selected = s.ReadVarInt();
        int size = s.ReadVarInt();
        var storage = new InventoryComponent(size);
        for (int i = 0; i < size; i++)
            storage[i] = StackCodec.Read(s);

        return new PlayerState(transform, health, new InventoryEntityComponent(storage) { SelectedSlot = selected });
    }
}
