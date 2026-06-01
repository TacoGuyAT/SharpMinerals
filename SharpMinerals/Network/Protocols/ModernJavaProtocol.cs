using Microsoft.Extensions.Logging;
using SharpMinerals.Level;
using SharpMinerals.Network.Buffers;
using SharpMinerals.Network.Messages;

namespace SharpMinerals.Network.Protocols;

/// <summary>
/// Base for the modern (post-Netty) Java protocol family: every packet is framed as
/// <c>VarInt(length) + VarInt(id) + body</c>, and a frame is read by slicing exactly
/// <c>length</c> bytes (so an unknown packet can be skipped). The whole Java version chain
/// (…→1.20.1→1.20.2→…) shares this framing; concrete versions add only their codecs, ids, and type
/// mapper. Keep this class framing-only (nothing version-specific) so any version in the chain can
/// extend it.
/// </summary>
public abstract class ModernJavaProtocol : Protocol {
    static readonly ILogger Log = Logging.For("Net.Protocol");

    public override byte[] Frame(IMessage message) {
        byte[] payload = EncodePayload(message); // VarInt id + codec body (with Types set on the stream)
        using var ms = new MemoryStream(payload.Length + MinecraftStream.VarIntMaxBytes);
        var s = new MinecraftStream(ms, leaveOpen: true);
        s.WriteVarInt(payload.Length);
        s.Write(payload, 0, payload.Length);
        return ms.ToArray();
    }

    public override IMessage? ReadMessage(MinecraftStream stream, ConnectionState state, PacketDirection direction) {
        int length = stream.ReadVarInt();
        if (length <= 0)
            throw new FormatException($"Illegal packet length {length}.");

        // Slice the declared length into its own buffer, then read id + body from that — so an
        // unknown packet's bytes are fully consumed and the connection stays in sync.
        byte[] body = stream.ReadBytes(length);
        // Expose this version's type mapper to decoders too (not just encoders), so a codec can map wire
        // ids back to our internal types (e.g. SlotWire.ReadStack resolving an item Slot → ItemStack).
        var payload = new MinecraftStream(new MemoryStream(body, writable: false)) { Types = Types };
        int id = payload.ReadVarInt();

        var codec = CodecFor(state, direction, id);
        if (codec is null) {
            Log.LogDebug("unknown packet 0x{Id:X2} in {State} — ignored", id, state);
            return null;
        }
        return codec.Decode(payload);
    }

    // Modern paletted chunk format + the Set Center Chunk preamble.
    public override IMessage BuildChunk(World world, int cx, int cz) => ChunkSerializer.Build(Types, world, cx, cz);
    public override IMessage? ChunkViewCenter(int cx, int cz) => new SetCenterChunkS2C(cx, cz);
}
