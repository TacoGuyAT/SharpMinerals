using SharpMinerals.Network.Buffers;

namespace SharpMinerals.Network;

/// <summary>
/// A protocol version: the table mapping message types to numeric packet ids and
/// the codecs that serialize them. Packet ids are namespaced by
/// <see cref="ConnectionState"/> and <see cref="PacketDirection"/>, so the same id
/// (e.g. 0x00) means different things in different states.
/// </summary>
public abstract class Protocol {
    readonly record struct WireKey(ConnectionState State, PacketDirection Direction, int Id);

    sealed record Entry(ConnectionState State, PacketDirection Direction, int Id, ICodec Codec);

    readonly Dictionary<Type, Entry> byType = new();
    readonly Dictionary<WireKey, Entry> byWire = new();

    /// <summary>The protocol version number sent in the handshake (e.g. 763 for 1.20.1).</summary>
    public abstract int Version { get; }

    /// <summary>The human-readable version name reported in the status response.</summary>
    public abstract string VersionName { get; }

    protected void Register<T>(ConnectionState state, PacketDirection direction, int id, ICodec<T> codec)
        where T : IMessage {
        var entry = new Entry(state, direction, id, codec);
        byType[typeof(T)] = entry;
        byWire[new WireKey(state, direction, id)] = entry;
    }

    /// <summary>Looks up the wire id assigned to a message type.</summary>
    public int IdOf(IMessage message) => Lookup(message.GetType()).Id;

    /// <summary>The codec used to encode an outgoing message.</summary>
    public ICodec CodecFor(IMessage message) => Lookup(message.GetType()).Codec;

    /// <summary>The codec for an incoming packet, or null if this version ignores it.</summary>
    public ICodec? CodecFor(ConnectionState state, PacketDirection direction, int id) =>
        byWire.TryGetValue(new WireKey(state, direction, id), out var e) ? e.Codec : null;

    Entry Lookup(Type type) =>
        byType.TryGetValue(type, out var e)
            ? e
            : throw new InvalidOperationException($"{GetType().Name} has no codec registered for {type.Name}.");

    /// <summary>Encodes a message's payload (without framing) into a fresh buffer.</summary>
    public byte[] EncodePayload(IMessage message) {
        var entry = Lookup(message.GetType());
        using var ms = new MemoryStream();
        var mc = new MinecraftStream(ms, leaveOpen: true);
        mc.WriteVarInt(entry.Id);
        entry.Codec.Encode(mc, message);
        return ms.ToArray();
    }
}
