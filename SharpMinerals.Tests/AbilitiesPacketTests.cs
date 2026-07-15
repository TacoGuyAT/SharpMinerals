using SharpMinerals.Entities;
using SharpMinerals.Network.Messages;
using SharpMinerals.Network.Protocols.JE763;
using Xunit;

namespace SharpMinerals.Tests;

/// <summary>Locks the wire format of the speed packets (the /speed and /flyspeed vehicles) for protocol 763.</summary>
public class AbilitiesPacketTests {
    [Fact]
    public void PlayerAbilitiesEncodesIdFlagsAndSpeeds() {
        // Flags map to the wire byte: Invulnerable->0x01, CanFly->0x04 (allow-flying), InstantBreak->0x08 (creative);
        // together 0x0D, with Flying (0x02) left off.
        var bytes = new ProtocolJE763().EncodePayload(new PlayerAbilitiesS2C(
            PlayerFlags.Invulnerable | PlayerFlags.CanFly | PlayerFlags.InstantBreak, 0.05f, 0.1f, false));
        Assert.Equal((byte)0x34, bytes[0]);                 // clientbound player_abilities id
        Assert.Equal((byte)0x0D, bytes[1]);                 // flags byte (invuln|allow-fly|creative)
        Assert.Equal(1 + 1 + 4 + 4, bytes.Length);          // id + flags + flyingSpeed + fovModifier
    }

    [Fact]
    public void UpdateAttributesEncodesIdEntityAndOneProperty() {
        var bytes = new ProtocolJE763().EncodePayload(new UpdateAttributesS2C(EntityId: 42, MovementSpeed: 0.2));
        Assert.Equal((byte)0x6A, bytes[0]);                 // entity_update_attributes id
        Assert.Equal((byte)42, bytes[1]);                   // entity id varint (small -> one byte)
        Assert.Equal((byte)1, bytes[2]);                    // exactly one attribute property follows
    }
}
