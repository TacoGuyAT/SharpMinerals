using SharpMinerals.Level;
using SharpMinerals.Network.Buffers;

namespace SharpMinerals.Network;

/// <summary>
/// A protocol version: the table mapping message types to packet ids and the codecs that serialize
/// them. Ids are namespaced by <see cref="ConnectionState"/> and <see cref="PacketDirection"/>.
/// </summary>
public abstract class Protocol {
    readonly record struct WireKey(ConnectionState State, PacketDirection Direction, int Id);

    sealed record Entry(ConnectionState State, PacketDirection Direction, int Id, ICodec Codec);

    readonly Dictionary<Type, Entry> byType = new();
    readonly Dictionary<WireKey, Entry> byWire = new();

    /// <summary>The protocol number sent in the handshake (e.g. 763 for 1.20.1).</summary>
    public abstract int Version { get; }

    public abstract string VersionName { get; }

    TypeMapper? typeMapper;

    /// <summary>This version's block/item ⇆ wire-id mapper — the single data-driven <see cref="TypeMapper"/>
    /// resolved for THIS protocol type from the registered mappings. Codecs read it off the stream during encode.</summary>
    public TypeMapper Types => typeMapper ??= new TypeMapper(GetType());

    /// <summary>Encodes a message into a complete, ready-to-write wire frame for this protocol's format.</summary>
    public abstract byte[] Frame(IMessage message);

    /// <summary>
    /// Reads and decodes one packet from <paramref name="stream"/>. Returns null for a frame this
    /// version has no codec for in the given state/direction. Framing-less protocols that cannot
    /// skip an unknown packet throw instead.
    /// </summary>
    public abstract IMessage? ReadMessage(MinecraftStream stream, ConnectionState state, PacketDirection direction);

    // Chunk streaming: the chunk wire format is the most protocol-divergent S2C payload, so the
    // protocol owns it; Streaming only decides which columns to send.

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

    public int IdOf(IMessage message) => Lookup(message.GetType()).Id;

    public ICodec CodecFor(IMessage message) => Lookup(message.GetType()).Codec;

    /// <summary>
    /// Whether this protocol can encode a message type. Callers silently drop ones it can't (e.g. a
    /// modern packet sent to a legacy client with no equivalent), so each protocol speaks only its own forms.
    /// </summary>
    public bool CanEncode(IMessage message) => byType.ContainsKey(message.GetType());

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
        var mc = new MinecraftStream(ms, leaveOpen: true) { Types = Types };
        mc.WriteVarInt(entry.Id);
        entry.Codec.Encode(mc, message);
        return ms.ToArray();
    }
}
