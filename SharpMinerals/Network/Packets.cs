using SharpMinerals.Network.Buffers;

namespace SharpMinerals.Network;

/// <summary>
/// The uncompressed packet framing used before the (optional) compression
/// threshold is negotiated: each packet is a VarInt length followed by that many
/// bytes of <c>VarInt packetId + payload</c>.
/// See https://minecraft.wiki/w/Java_Edition_protocol#Packet_format.
/// </summary>
public static class Packets {
    /// <summary>A decoded packet frame: its id and a reader over the remaining payload.</summary>
    public readonly record struct Frame(int Id, MinecraftStream Payload);

    /// <summary>Reads one length-prefixed frame from <paramref name="source"/>.</summary>
    public static Frame Read(MinecraftStream source) {
        int length = source.ReadVarInt();
        if (length <= 0)
            throw new FormatException($"Illegal packet length {length}.");

        var body = source.ReadBytes(length);
        var payload = new MinecraftStream(new MemoryStream(body, writable: false));
        int id = payload.ReadVarInt();
        return new Frame(id, payload);
    }

    /// <summary>
    /// Frames and writes a message to <paramref name="destination"/>, returning the
    /// payload size (packet id + body, before the length prefix).
    /// </summary>
    public static int Write(MinecraftStream destination, Protocol protocol, IMessage message) {
        byte[] payload = protocol.EncodePayload(message);
        destination.WriteVarInt(payload.Length);
        destination.Write(payload, 0, payload.Length);
        destination.Flush();
        return payload.Length;
    }
}
