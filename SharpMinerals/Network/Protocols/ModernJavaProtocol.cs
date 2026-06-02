using Microsoft.Extensions.Logging;
using SharpMinerals.Level;
using SharpMinerals.Network.Buffers;
using SharpMinerals.Network.Messages;

namespace SharpMinerals.Network.Protocols;

/// <summary>
/// Base for the modern (post-Netty) Java protocol family: packets framed as <c>VarInt(length) +
/// VarInt(id) + body</c>, read by slicing exactly <c>length</c> bytes so unknown packets can be skipped.
/// Framing-only; concrete versions add codecs, ids, and a type mapper.
/// </summary>
public abstract class ModernJavaProtocol : Protocol {
    static readonly ILogger Log = Logging.For("Net.Protocol");

    public override byte[] Frame(IMessage message) {
        byte[] payload = EncodePayload(message); // VarInt id + codec body
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

        // Slice the declared length into its own buffer so an unknown packet stays fully consumed.
        byte[] body = stream.ReadBytes(length);
        // Expose Types to decoders too, so a codec can map wire ids back to our internal types.
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
