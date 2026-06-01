using SharpMinerals.Network.Buffers;
using SharpMinerals.Network.Messages;

namespace SharpMinerals.Network.Protocols.JE763.Codecs;

internal sealed class SystemChatMessageS2CCodec : ICodec<SystemChatMessageS2C> {
    public void Encode(MinecraftStream s, SystemChatMessageS2C m) {
        s.WriteString(m.Content.ToString()); // JSON chat component (1.20.1)
        s.WriteBool(m.Overlay);
    }

    public SystemChatMessageS2C Decode(MinecraftStream s) =>
        throw new NotSupportedException("SystemChatMessageS2C is clientbound only.");
}

internal sealed class PlayerListHeaderFooterS2CCodec : ICodec<PlayerListHeaderFooterS2C> {
    public void Encode(MinecraftStream s, PlayerListHeaderFooterS2C m) {
        s.WriteString(m.Header.ToString()); // JSON chat components (1.20.1)
        s.WriteString(m.Footer.ToString());
    }

    public PlayerListHeaderFooterS2C Decode(MinecraftStream s) =>
        throw new NotSupportedException("PlayerListHeaderFooterS2C is clientbound only.");
}

internal sealed class ChatMessageC2SCodec : ICodec<ChatMessageC2S> {
    public void Encode(MinecraftStream s, ChatMessageC2S m) => s.WriteString(m.Message);

    // Read the message; the rest (timestamp, salt, signature, acknowledged) is ignored.
    public ChatMessageC2S Decode(MinecraftStream s) {
        var message = s.ReadString(256);
        s.ReadRemaining();
        return new ChatMessageC2S(message);
    }
}

internal sealed class ChatCommandC2SCodec : ICodec<ChatCommandC2S> {
    public void Encode(MinecraftStream s, ChatCommandC2S m) => s.WriteString(m.Command);

    public ChatCommandC2S Decode(MinecraftStream s) {
        var command = s.ReadString(256);
        s.ReadRemaining();
        return new ChatCommandC2S(command);
    }
}
