using SharpMinerals.Level;
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

    /// <summary>This version's block/item ⇆ wire-id mapper. Codecs read it off the stream during encode.</summary>
    public abstract ITypeMapper Types { get; }

    /// <summary>Encodes a message into a complete, ready-to-write wire frame for this protocol's format.</summary>
    public abstract byte[] Frame(IMessage message);

    /// <summary>
    /// Reads and decodes one packet from <paramref name="stream"/> per this protocol's wire format
    /// (deframe → packet id → codec dispatch → decode). Returns null for a frame this version has no
    /// codec for in the given state/direction (the caller keeps reading). Framing-less protocols that
    /// cannot skip an unknown packet throw instead.
    /// </summary>
    public abstract IMessage? ReadMessage(MinecraftStream stream, ConnectionState state, PacketDirection direction);

    // ── Chunk streaming (the chunk wire format is the most protocol-divergent S2C payload, so the
    //    protocol owns it — ChunkStreamer just decides WHICH columns to send, not how) ──

    /// <summary>Builds this protocol's "chunk data" message for one column of the world.</summary>
    public abstract IMessage BuildChunk(World world, int cx, int cz);

    /// <summary>
    /// The "set view centre" message to send before a batch of columns, or null if the protocol has no
    /// such concept (legacy 1.5.2 has none; modern Java needs Set Center Chunk or it drops the columns).
    /// </summary>
    public virtual IMessage? ChunkViewCenter(int cx, int cz) => null;

    /// <summary>Column radius streamed around a player for this protocol (legacy uses a smaller one).</summary>
    public virtual int ChunkViewRadius => 5;

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

    /// <summary>
    /// Whether this protocol can encode a message type. A connection silently drops messages it can't
    /// encode (e.g. a modern inventory/entity packet sent to a legacy client that has no equivalent) —
    /// the cross-protocol seam: each protocol speaks only its own wire forms, never crashing on others'.
    /// </summary>
    public bool CanEncode(IMessage message) => byType.ContainsKey(message.GetType());

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
        var mc = new MinecraftStream(ms, leaveOpen: true) { Types = Types }; // codecs map ids via this
        mc.WriteVarInt(entry.Id);
        entry.Codec.Encode(mc, message);
        return ms.ToArray();
    }
}
