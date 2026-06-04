namespace SharpMinerals.Network.Messages;

// Legacy (pre-Netty, JE61/1.5.2) protocol-mechanics messages. These have no version-agnostic
// game-domain meaning - they are framing/handshake mechanics specific to the legacy wire format.

/// <summary>Legacy Server List Ping (0xFE); carries the trailing "magic" byte (1 for the 1.5.2 ping).</summary>
public sealed record LegacyServerListPingC2S(byte Magic) : IMessage;

/// <summary>Legacy Disconnect/Kick (0xFF): a UTF-16BE reason; also doubles as the server-list-ping response.</summary>
public sealed record LegacyKickS2C(string Text) : IMessage;

// -- Legacy login handshake (0x02 -> 0xFD -> 0xFC -> 0xCD -> 0x01), see Protocol Encryption ----------

/// <summary>Handshake (0x02): the client opens login with its protocol version, name, and the host it dialed.</summary>
public sealed record LegacyHandshakeC2S(byte ProtocolVersion, string Username, string Host, int Port) : IMessage;

/// <summary>Encryption Key Request (0xFD): server id ("-" offline), RSA public key (X.509 SPKI), random verify token.</summary>
public sealed record LegacyEncryptionRequestS2C(string ServerId, byte[] PublicKey, byte[] VerifyToken) : IMessage;

/// <summary>Encryption Key Response (0xFC): the 16-byte AES shared secret and verify token, each RSA/PKCS#1-encrypted.</summary>
public sealed record LegacyEncryptionResponseC2S(byte[] SharedSecret, byte[] VerifyToken) : IMessage;

/// <summary>Encryption Key Response (0xFC): server -> client, EMPTY payload - the signal to enable AES/CFB8.</summary>
public sealed record LegacyEncryptionAcceptS2C : IMessage;

/// <summary>Client Statuses (0xCD): client -> server. Payload 0 = "initial spawn" (ready for login).</summary>
public sealed record LegacyClientStatusesC2S(byte Payload) : IMessage;

/// <summary>Login Request (0x01): server -> client. Establishes the player's entity id and world params.</summary>
public sealed record LegacyLoginRequestS2C(
    int EntityId, string LevelType, byte GameMode, byte Dimension, byte Difficulty, byte MaxPlayers) : IMessage;

// -- Post-login client chatter (accepted/ignored for now; world streaming is the next milestone) --

/// <summary>Client Settings (0xCC): sent after login.</summary>
public sealed record LegacyClientSettingsC2S(string Locale, byte ViewDistance, byte ChatFlags, byte Difficulty, bool ShowCape) : IMessage;

/// <summary>Plugin Message (0xFA): a channel name + opaque payload (e.g. the client's brand).</summary>
public sealed record LegacyPluginMessageC2S(string Channel, byte[] Data) : IMessage;

// 0x0A Player (onGround only - no position; no generic equivalent, so decoded and ignored). 0x0B/0x0C/0x0D
// movement and 0x0E digging decode directly into the GENERIC messages (see LegacyServerboundCodecs).
public sealed record LegacyPlayerC2S(bool OnGround) : IMessage;

// -- Remaining serverbound packets - decoded so the (length-prefix-free) stream never desyncs.
//    Most are ignored for now; wire up individually as features land. (Item-bearing packets consume
//    the Slot via SkipLegacySlot and don't carry it yet.) --
// 0x03 Chat (serverbound) decodes into the GENERIC ChatMessageC2S (see LegacyChatCodecs).
public sealed record LegacyUseEntityC2S(int User, int Target, bool MouseButton) : IMessage;          // 0x07
public sealed record LegacyBlockPlacementC2S(int X, byte Y, int Z, byte Direction, short ItemId, short Damage, byte CursorX, byte CursorY, byte CursorZ) : IMessage; // 0x0F
public sealed record LegacyHeldItemChangeC2S(short Slot) : IMessage;                                  // 0x10
// 0x12 Animation (the arm swing) decodes into the GENERIC SwingArmC2S (see LegacyServerboundCodecs).
// 0x13 Entity Action (sneak/sprint toggle) decodes into the GENERIC EntityActionC2S (see LegacyServerboundCodecs).
public sealed record LegacyCloseWindowC2S(byte WindowId) : IMessage;                                 // 0x65
public sealed record LegacyClickWindowC2S(byte WindowId, short Slot, byte Button, short ActionNumber, byte Mode) : IMessage; // 0x66
public sealed record LegacyConfirmTransactionC2S(byte WindowId, short ActionNumber, bool Accepted) : IMessage; // 0x6A
public sealed record LegacyCreativeActionC2S(short Slot) : IMessage;                                 // 0x6B
public sealed record LegacyEnchantItemC2S(byte WindowId, byte Enchantment) : IMessage;               // 0x6C
public sealed record LegacyUpdateSignC2S(int X, short Y, int Z, string Text1, string Text2, string Text3, string Text4) : IMessage; // 0x82
public sealed record LegacyPlayerAbilitiesC2S(byte Flags, byte FlyingSpeed, byte WalkingSpeed) : IMessage; // 0xCA
public sealed record LegacyTabCompleteC2S(string Text) : IMessage;                                   // 0xCB
public sealed record LegacyDisconnectC2S(string Reason) : IMessage;                                  // 0xFF

// (Keep Alive uses the GENERIC KeepAliveS2C/KeepAliveC2S messages, mapped to the legacy 0x00 int
//  form by per-protocol codecs in JE61 - see LegacyServerboundCodecs / LegacyWorldCodecs.)

// -- World streaming (server -> client) -------------------------------------------

/// <summary>Keep Alive (0x00): server -> client; the client echoes it. Sent ~1/s to avoid timeout.</summary>
public sealed record LegacyKeepAliveS2C(int Id) : IMessage;

/// <summary>Spawn Position (0x06): the compass target / world spawn (block coords).</summary>
public sealed record LegacySpawnPositionS2C(int X, int Y, int Z) : IMessage;

/// <summary>
/// Player Position and Look (0x0D): positions the player, unlocks "Downloading terrain".
/// 1.5.2 quirk: clientbound field order is X, Stance, Y (swapped vs serverbound), Z; codec writes Stance = Y + eye height.
/// </summary>
public sealed record LegacyPlayerPositionLookS2C(
    double X, double Y, double Z, float Yaw, float Pitch, bool OnGround) : IMessage;

/// <summary>Chunk Data (0x33): one column; section arrays pre-built and zlib-compressed by <see cref="LegacyChunkSerializer"/>.</summary>
public sealed record LegacyChunkDataS2C(
    int X, int Z, bool GroundUpContinuous, int PrimaryBitmap, int AddBitmap, byte[] CompressedData) : IMessage;
