using SharpMinerals.Level;
using SharpMinerals.Network.Buffers;

namespace SharpMinerals.Network.Protocols;

/// <summary>
/// Base for the legacy (pre-Netty, ≤1.6) Java protocol family. The wire format is radically
/// different from <see cref="ModernJavaProtocol"/>: a packet is just <c>[1-byte id][fields]</c> with
/// <b>no length prefix and no compression</b>, and there is <b>no connection-state partitioning</b> —
/// ids are global. Two consequences drive this class:
/// <list type="bullet">
/// <item>Framing reads/writes a single id byte, and fields are decoded straight off the live stream
/// (each field is exact-size), since there is no length to slice by.</item>
/// <item>An unknown id is <b>unrecoverable</b> — without a length we cannot skip it, so the stream is
/// desynced and we must bail (the caller disconnects).</item>
/// </list>
/// Because there are no states, all codecs are registered and looked up under one fixed state, so
/// dispatch is independent of <see cref="NetClient.State"/>.
/// </summary>
public abstract class LegacyJavaProtocol : Protocol {
    /// <summary>The single state every legacy codec is registered under (legacy has no state concept).</summary>
    protected const ConnectionState LegacyState = ConnectionState.Handshaking;

    public override byte[] Frame(IMessage message) {
        using var ms = new MemoryStream();
        var s = new MinecraftStream(ms, leaveOpen: true) { Types = Types }; // codecs map ids via this
        s.WriteUByte((byte)IdOf(message)); // single-byte id, NO length prefix
        CodecFor(message).Encode(s, message);
        return ms.ToArray();
    }

    public override IMessage? ReadMessage(MinecraftStream stream, ConnectionState state, PacketDirection direction) {
        int id = stream.ReadUByte();
        var codec = CodecFor(LegacyState, direction, id);
        if (codec is null)
            // No length prefix ⇒ we can't skip an unknown packet; the connection is desynced.
            throw new FormatException($"Unknown legacy packet id 0x{id:X2} ({direction}) — cannot resync.");
        return codec.Decode(stream); // decodes straight off the live stream
    }

    // Pre-Anvil 1.5.2 chunk format; legacy has no Set-Center-Chunk and uses a smaller radius (heavier build).
    public override IMessage BuildChunk(World world, int cx, int cz) => LegacyChunkSerializer.Build(Types, world, cx, cz);
    public override int ChunkViewRadius => 3;
}
