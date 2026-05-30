using SharpMinerals.Chat;
using SharpMinerals.Network.Buffers;
using SharpMinerals.Network.Messages;

namespace SharpMinerals.Network.Protocols.JE763.Codecs;

internal sealed class LoginStartC2SCodec : ICodec<LoginStartC2S> {
    public void Encode(MinecraftStream s, LoginStartC2S m) {
        s.WriteString(m.Name);
        s.WriteUuid(m.ProfileId);
    }

    public LoginStartC2S Decode(MinecraftStream s) => new(s.ReadString(16), s.ReadUuid());
}

internal sealed class LoginDisconnectS2CCodec : ICodec<LoginDisconnectS2C> {
    // The login Disconnect reason is a JSON chat component string (not NBT).
    public void Encode(MinecraftStream s, LoginDisconnectS2C m) => s.WriteString(m.Reason.ToString());
    public LoginDisconnectS2C Decode(MinecraftStream s) => new(ChatComponent.FromJson(s.ReadString()));
}

internal sealed class LoginSuccessS2CCodec : ICodec<LoginSuccessS2C> {
    public void Encode(MinecraftStream s, LoginSuccessS2C m) {
        s.WriteUuid(m.Uuid);
        s.WriteString(m.Name);
        s.WriteVarInt(0); // number of signed profile properties (none in offline mode)
    }

    public LoginSuccessS2C Decode(MinecraftStream s) {
        var uuid = s.ReadUuid();
        var name = s.ReadString(16);
        int properties = s.ReadVarInt();
        for (int i = 0; i < properties; i++) {
            s.ReadString();              // name
            s.ReadString();              // value
            if (s.ReadBool())            // optional signature
                s.ReadString();
        }
        return new LoginSuccessS2C(uuid, name);
    }
}
