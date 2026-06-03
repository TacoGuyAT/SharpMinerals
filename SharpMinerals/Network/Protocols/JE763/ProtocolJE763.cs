using SharpMinerals.Network.Protocols.JE762;
using SharpMinerals.Network.Protocols.JE762.Codecs;

namespace SharpMinerals.Network.Protocols.JE763;

/// <summary>
/// Java Edition protocol 763 (Minecraft 1.20.1). The packet ids are identical to 1.19.4 (<see cref="ProtocolJE762"/>);
/// the wire-id deltas are data (registered for <c>ProtocolJE763</c> in the minecraft mod, resolved by the shared
/// <see cref="TypeMapper"/>). The remaining differences are three 1.20 packet-body tweaks: Join Game + Respawn gained
/// a trailing portal-cooldown VarInt, and Chunk Data dropped the trust-edges bool. This re-registers those two codecs
/// in their 1.20 shape (overwriting the inherited 1.19.4 ones) and flips off <see cref="ChunkDataHasTrustEdges"/>.
/// </summary>
// Not sealed: a future ProtocolJE765 extends this one.
public class ProtocolJE763 : ProtocolJE762 {
    public override int Version => 763;
    public override string VersionName => "1.20.1";

    protected override bool ChunkDataHasTrustEdges => false;

    public ProtocolJE763() {
        // 1.20 appended a portal-cooldown VarInt to Join Game and Respawn (same ids as 1.19.4).
        Register(ConnectionState.Play, PacketDirection.Clientbound, Cb.JoinGame, new JoinGameS2CCodec(portalCooldown: true));
        Register(ConnectionState.Play, PacketDirection.Clientbound, Cb.Respawn, new RespawnS2CCodec(portalCooldown: true));
    }
}
