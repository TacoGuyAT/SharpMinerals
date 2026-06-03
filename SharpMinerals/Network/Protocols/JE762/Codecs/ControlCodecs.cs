using SharpMinerals.Network.Buffers;
using SharpMinerals.Network.Messages;

namespace SharpMinerals.Network.Protocols.JE762.Codecs;

/// <summary>The test-harness control channel name.</summary>
internal static class ControlChannel {
    public const string Name = "sharptester:cmd";
}

internal sealed class TestCommandS2CCodec : ICodec<TestCommandS2C> {
    // Custom Payload = Identifier channel + data; the data here is a single string.
    public void Encode(MinecraftStream s, TestCommandS2C m) {
        s.WriteString(ControlChannel.Name);
        s.WriteString(m.Command);
    }

    public TestCommandS2C Decode(MinecraftStream s) =>
        throw new NotSupportedException("TestCommandS2C is clientbound only.");
}

internal sealed class BrandS2CCodec : ICodec<BrandS2C> {
    // Custom Payload on the minecraft:brand channel; the data is a single string.
    public void Encode(MinecraftStream s, BrandS2C m) {
        s.WriteString("minecraft:brand");
        s.WriteString(m.Brand);
    }

    public BrandS2C Decode(MinecraftStream s) =>
        throw new NotSupportedException("BrandS2C is clientbound only.");
}

internal sealed class CustomPayloadC2SCodec : ICodec<CustomPayloadC2S> {
    public void Encode(MinecraftStream s, CustomPayloadC2S m) {
        s.WriteString(m.Channel);
        s.Write(m.Data, 0, m.Data.Length);
    }

    public CustomPayloadC2S Decode(MinecraftStream s) {
        string channel = s.ReadString();
        return new CustomPayloadC2S(channel, s.ReadRemaining());
    }
}
