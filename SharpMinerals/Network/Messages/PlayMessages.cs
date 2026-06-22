using SharpMinerals.Blocks;
using SharpMinerals.Entities;
using SharpMinerals.Items;
using SharpMinerals.Math;

namespace SharpMinerals.Network.Messages;

// -- Play: clientbound -------------------------------------------------------

/// <summary>Bundle delimiter (0x00): brackets a group of packets the client must apply together in one tick.</summary>
public sealed record BundleDelimiterS2C : IMessage;

/// <summary>The first play packet (minimal field subset).</summary>
public sealed record JoinGameS2C(
    int EntityId,
    byte GameMode,
    string DimensionName,
    long HashedSeed,
    int ViewDistance,
    bool ReducedDebugInfo) : IMessage;

/// <summary>
/// Respawn (0x41): re-initialises the client into a dimension without a reconnect (the world-switch mechanism).
/// <see cref="DimensionType"/> must be a type the registry codec (sent with Join Game) defines.
/// </summary>
public sealed record RespawnS2C(string DimensionType, string WorldName, long HashedSeed, byte GameMode, bool IsFlat) : IMessage;

/// <summary>Server -> client liveness probe; the client echoes the id back.</summary>
public sealed record KeepAliveS2C(long Id) : IMessage;

public sealed record SetHealthS2C(float Health, int Food, float Saturation) : IMessage;

/// <summary>Player Abilities (0x34): sets the ability flag bits, the fly speed, and the FOV/walk-speed modifier.</summary>
public sealed record PlayerAbilitiesS2C(byte Flags, float FlyingSpeed, float FieldOfViewModifier) : IMessage;

/// <summary>Update Attributes (0x6A) carrying just the player's <c>generic.movement_speed</c> (walk speed).</summary>
public sealed record UpdateAttributesS2C(int EntityId, double MovementSpeed) : IMessage;

/// <summary>Sets the centre of the client's loaded area. Required before streaming chunks, or the client discards them.</summary>
public sealed record SetCenterChunkS2C(int ChunkX, int ChunkZ) : IMessage;

/// <summary>Sets the world spawn point (compass target / respawn anchor).</summary>
public sealed record SetDefaultSpawnPositionS2C(Vector3i Position, float Angle) : IMessage;

/// <summary>Teleports/positions the player; the client confirms with the teleport id.</summary>
public sealed record SynchronizePlayerPositionS2C(
    double X, double Y, double Z, float Yaw, float Pitch, int TeleportId) : IMessage;

/// <summary>
/// A single block changed. Carries our <see cref="BlockType"/> plus an optional <see cref="BlockState"/>
/// (null = default); the codec maps it to the connection's wire state id.
/// </summary>
public sealed record BlockUpdateS2C(Vector3i Position, BlockType Block, BlockState? State = null) : IMessage;

/// <summary>Block Action ("block event"): runs a block-specific animation at <see cref="Position"/>. For a chest,
/// <see cref="ActionId"/> is 1 ("number of viewers") and <see cref="Param"/> is the open-viewer count - the lid
/// opens while it's &gt; 0 and closes at 0. <see cref="BlockType"/> is inferred from the position by the client
/// (the Notchian client ignores the field), so it is left 0.</summary>
public sealed record BlockActionS2C(Vector3i Position, byte ActionId, byte Param, int BlockType = 0) : IMessage;

/// <summary>Acks a dig/place by echoing its sequence number so the client reconciles its predicted change.</summary>
public sealed record AckBlockChangeS2C(int Sequence) : IMessage;

/// <summary>Chunk Data and Update Light (0x24); body pre-serialized by <see cref="ChunkSerializer"/> and written verbatim.</summary>
public sealed record ChunkDataS2C(byte[] Payload) : IMessage;

/// <summary>
/// Sets a dropped-item entity's contents via entity metadata. Must follow <see cref="SpawnEntityS2C"/>,
/// or the client renders an empty item (and some client mods crash). Carries our <see cref="ItemStack"/>.
/// </summary>
public sealed record SetItemEntityMetadataS2C(int EntityId, ItemStack Stack) : IMessage;

public sealed record SpawnEntityS2C(
    int EntityId,
    Guid Uuid,
    EntityType Type,
    double X, double Y, double Z,
    byte Pitch, byte Yaw, byte HeadYaw,
    int Data,
    short VelocityX, short VelocityY, short VelocityZ,
    // For a falling_block, the spawn Data is the carried block's per-protocol state id; when set, the
    // codec resolves it via the connection's type mapper and writes it as Data (overriding the int above).
    BlockType? BlockData = null) : IMessage;

/// <summary>Set Entity Velocity (0x54): an entity's velocity in units of 1/8000 of a block per tick.
/// The client applies this explicitly - more reliable than the velocity carried by Spawn Entity.</summary>
public sealed record SetEntityVelocityS2C(int EntityId, short VelocityX, short VelocityY, short VelocityZ) : IMessage;

/// <summary>Collect Item (0x67): plays the "item flies into the collector" pickup animation. Purely
/// cosmetic - the item entity is still removed separately (Remove Entities) or its count updated.</summary>
public sealed record CollectItemS2C(int CollectedEntityId, int CollectorEntityId, int PickupItemCount) : IMessage;

// -- Play: serverbound -------------------------------------------------------

public sealed record KeepAliveC2S(long Id) : IMessage;

public sealed record ConfirmTeleportationC2S(int TeleportId) : IMessage;

public sealed record SetPlayerPositionC2S(double X, double Y, double Z, bool OnGround) : IMessage;

/// <summary>Player Abilities (serverbound, 0x1C): the client reports its flag changes. Only the Flying bit is
/// meaningful to the server (it toggles as the player starts/stops flying).</summary>
public sealed record PlayerAbilitiesC2S(byte Flags) : IMessage;

/// <summary>
/// Block digging. <see cref="Status"/>: 0 started (also creative instant-break), 1 cancelled, 2 finished (survival).
/// </summary>
public sealed record PlayerActionC2S(int Status, Vector3i Position, byte Face, int Sequence) : IMessage;

/// <summary>Use an item against a block (placement on the adjacent block in the <see cref="Face"/> direction).</summary>
public sealed record UseItemOnC2S(
    int Hand,
    Vector3i Position,
    int Face,
    float CursorX, float CursorY, float CursorZ,
    bool InsideBlock,
    int Sequence) : IMessage;

/// <summary>The six block faces, ordered as the protocol encodes them.</summary>
public enum BlockFace {
    Bottom = 0, // -Y
    Top = 1,    // +Y
    North = 2,  // -Z
    South = 3,  // +Z
    West = 4,   // -X
    East = 5,   // +X
}
