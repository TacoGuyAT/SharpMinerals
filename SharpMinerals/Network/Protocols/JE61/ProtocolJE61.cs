using System.Security.Cryptography;
using SharpMinerals.Network.Protocols.JE61.Codecs;

namespace SharpMinerals.Network.Protocols.JE61;

/// <summary>
/// Java Edition protocol 61 (Minecraft 1.5.2), the legacy pre-Netty protocol: no length framing,
/// single-byte global ids, UTF-16BE strings, zlib chunk columns. Framing lives in <see cref="LegacyJavaProtocol"/>.
/// </summary>
public sealed class ProtocolJE61 : LegacyJavaProtocol {
    public override int Version => 61;
    public override string VersionName => "1.5.2";

    // RSA keypair for the login encryption handshake (1024-bit per the protocol); one per server.
    readonly RSA rsa = RSA.Create(1024);

    /// <summary>The server's RSA public key as X.509 SubjectPublicKeyInfo (the wire form in 0xFD).</summary>
    public byte[] PublicKeyDer => rsa.ExportSubjectPublicKeyInfo();

    /// <summary>Decrypts a PKCS#1-v1.5 blob (shared secret / verify token) from a 0xFC with the private key.</summary>
    public byte[] DecryptRsa(byte[] data) => rsa.Decrypt(data, RSAEncryptionPadding.Pkcs1);

    /// <summary>Packet ids (single byte, global - legacy has no state/direction id namespacing).</summary>
    static class Id {
        public const int KeepAlive = 0x00;          // two-way (int id)
        public const int LoginRequest = 0x01;       // S->C
        public const int Handshake = 0x02;          // C->S (login)
        public const int Chat = 0x03;               // two-way
        public const int EntityEquipment = 0x05;    // S->C (held item + armour)
        public const int SpawnPosition = 0x06;      // S->C
        public const int UseEntity = 0x07;          // C->S
        public const int Player = 0x0A;             // C->S
        public const int PlayerPosition = 0x0B;     // C->S
        public const int PlayerLook = 0x0C;         // C->S
        public const int PlayerPositionLook = 0x0D; // two-way
        public const int PlayerDigging = 0x0E;      // C->S
        public const int BlockPlacement = 0x0F;     // C->S
        public const int SpawnNamedEntity = 0x14;   // S->C (other players)
        public const int DestroyEntity = 0x1D;      // S->C
        public const int EntityTeleport = 0x22;     // S->C
        public const int EntityHeadLook = 0x23;     // S->C
        public const int EntityMetadata = 0x28;      // S->C
        public const int HeldItemChange = 0x10;     // two-way
        public const int Animation = 0x12;          // two-way
        public const int EntityAction = 0x13;       // C->S
        public const int ChunkData = 0x33;          // S->C
        public const int BlockChange = 0x35;        // S->C
        public const int CloseWindow = 0x65;        // two-way
        public const int ClickWindow = 0x66;        // C->S
        public const int ConfirmTransaction = 0x6A; // two-way
        public const int CreativeAction = 0x6B;     // two-way
        public const int EnchantItem = 0x6C;        // C->S
        public const int UpdateSign = 0x82;         // two-way
        public const int PlayerAbilities = 0xCA;    // two-way
        public const int TabComplete = 0xCB;        // two-way
        public const int ClientSettings = 0xCC;     // C->S
        public const int ClientStatuses = 0xCD;     // C->S
        public const int PluginMessage = 0xFA;      // two-way
        public const int EncryptionResponse = 0xFC; // C->S response / S->C empty accept
        public const int EncryptionRequest = 0xFD;  // S->C
        public const int ServerListPing = 0xFE;     // C->S
        public const int Disconnect = 0xFF;         // two-way: kick / ping response
    }

    public ProtocolJE61() {
        // Legacy has no connection states; everything registers under the base's single LegacyState.
        // -- Clientbound ----------------------------------------------------------
        Register(LegacyState, PacketDirection.Clientbound, Id.KeepAlive, new LegacyKeepAliveS2CMapper()); // generic message
        Register(LegacyState, PacketDirection.Clientbound, Id.LoginRequest, new LegacyLoginRequestS2CCodec());
        Register(LegacyState, PacketDirection.Clientbound, Id.SpawnPosition, new LegacySpawnPositionS2CCodec());
        Register(LegacyState, PacketDirection.Clientbound, Id.PlayerPositionLook, new LegacyPlayerPositionLookS2CCodec());
        Register(LegacyState, PacketDirection.Clientbound, Id.ChunkData, new LegacyChunkDataS2CCodec());
        Register(LegacyState, PacketDirection.Clientbound, Id.BlockChange, new LegacyBlockChangeS2CMapper()); // generic message
        Register(LegacyState, PacketDirection.Clientbound, Id.Chat, new LegacyChatS2CMapper()); // generic SystemChatMessageS2C -> §-string
        // Entity visibility (other players) - generic messages mapped to legacy wire forms.
        Register(LegacyState, PacketDirection.Clientbound, Id.SpawnNamedEntity, new LegacySpawnNamedEntityS2CMapper());
        Register(LegacyState, PacketDirection.Clientbound, Id.DestroyEntity, new LegacyDestroyEntityS2CMapper());
        Register(LegacyState, PacketDirection.Clientbound, Id.EntityTeleport, new LegacyEntityTeleportS2CMapper());
        Register(LegacyState, PacketDirection.Clientbound, Id.EntityHeadLook, new LegacyEntityHeadLookS2CMapper());
        Register(LegacyState, PacketDirection.Clientbound, Id.Animation, new LegacyEntityAnimationS2CMapper());
        Register(LegacyState, PacketDirection.Clientbound, Id.EntityMetadata, new LegacyEntityMetadataS2CMapper());
        Register(LegacyState, PacketDirection.Clientbound, Id.EntityEquipment, new LegacyEntityEquipmentS2CMapper());
        Register(LegacyState, PacketDirection.Clientbound, Id.EncryptionRequest, new LegacyEncryptionRequestS2CCodec());
        Register(LegacyState, PacketDirection.Clientbound, Id.EncryptionResponse, new LegacyEncryptionAcceptS2CCodec());
        Register(LegacyState, PacketDirection.Clientbound, Id.Disconnect, new LegacyKickS2CCodec());

        // -- Serverbound - EVERY packet is decoded (legacy can't skip unknowns) ------
        // Login handshake:
        Register(LegacyState, PacketDirection.Serverbound, Id.Handshake, new LegacyHandshakeC2SCodec());
        Register(LegacyState, PacketDirection.Serverbound, Id.EncryptionResponse, new LegacyEncryptionResponseC2SCodec());
        Register(LegacyState, PacketDirection.Serverbound, Id.ClientStatuses, new LegacyClientStatusesC2SCodec());
        Register(LegacyState, PacketDirection.Serverbound, Id.ServerListPing, new LegacyServerListPingC2SCodec());
        // Keep-alive uses the GENERIC message, mapped to the legacy int form.
        Register(LegacyState, PacketDirection.Serverbound, Id.KeepAlive, new LegacyKeepAliveC2SMapper());
        // In-world chatter / movement / interaction - decoded; wire to game logic as features land.
        Register(LegacyState, PacketDirection.Serverbound, Id.Chat, new LegacyChatToGenericCodec()); // -> generic ChatMessageC2S
        Register(LegacyState, PacketDirection.Serverbound, Id.UseEntity, new LegacyUseEntityC2SCodec());
        Register(LegacyState, PacketDirection.Serverbound, Id.Player, new LegacyPlayerC2SCodec()); // onGround only - ignored
        // Movement + digging decode into the GENERIC intermediary messages (shared handlers, no legacy path):
        Register(LegacyState, PacketDirection.Serverbound, Id.PlayerPosition, new LegacyPositionToGenericCodec());
        Register(LegacyState, PacketDirection.Serverbound, Id.PlayerLook, new LegacyLookToGenericCodec());
        Register(LegacyState, PacketDirection.Serverbound, Id.PlayerPositionLook, new LegacyPositionLookToGenericCodec());
        Register(LegacyState, PacketDirection.Serverbound, Id.PlayerDigging, new LegacyDiggingToGenericCodec());
        Register(LegacyState, PacketDirection.Serverbound, Id.BlockPlacement, new LegacyBlockPlacementC2SCodec());
        Register(LegacyState, PacketDirection.Serverbound, Id.HeldItemChange, new LegacyHeldItemChangeC2SCodec());
        Register(LegacyState, PacketDirection.Serverbound, Id.Animation, new LegacySwingToGenericCodec()); // -> generic SwingArmC2S
        Register(LegacyState, PacketDirection.Serverbound, Id.EntityAction, new LegacyEntityActionToGenericCodec()); // -> generic EntityActionC2S
        Register(LegacyState, PacketDirection.Serverbound, Id.CloseWindow, new LegacyCloseWindowC2SCodec());
        Register(LegacyState, PacketDirection.Serverbound, Id.ClickWindow, new LegacyClickWindowC2SCodec());
        Register(LegacyState, PacketDirection.Serverbound, Id.ConfirmTransaction, new LegacyConfirmTransactionC2SCodec());
        Register(LegacyState, PacketDirection.Serverbound, Id.CreativeAction, new LegacyCreativeActionC2SCodec());
        Register(LegacyState, PacketDirection.Serverbound, Id.EnchantItem, new LegacyEnchantItemC2SCodec());
        Register(LegacyState, PacketDirection.Serverbound, Id.UpdateSign, new LegacyUpdateSignC2SCodec());
        Register(LegacyState, PacketDirection.Serverbound, Id.PlayerAbilities, new LegacyPlayerAbilitiesC2SCodec());
        Register(LegacyState, PacketDirection.Serverbound, Id.TabComplete, new LegacyTabCompleteC2SCodec());
        Register(LegacyState, PacketDirection.Serverbound, Id.ClientSettings, new LegacyClientSettingsC2SCodec());
        Register(LegacyState, PacketDirection.Serverbound, Id.PluginMessage, new LegacyPluginMessageC2SCodec());
        Register(LegacyState, PacketDirection.Serverbound, Id.Disconnect, new LegacyDisconnectC2SCodec());
    }
}
