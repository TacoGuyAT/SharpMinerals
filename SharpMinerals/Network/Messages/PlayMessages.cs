using SharpMinerals.Blocks;
using SharpMinerals.Entities;
using SharpMinerals.Items;
using SharpMinerals.Math;

namespace SharpMinerals.Network.Messages;

// ── Play: clientbound ───────────────────────────────────────────────────────

/// <summary>
/// Bundle delimiter (0x00). Sent before and after a group of packets the client
/// must apply together in one tick — e.g. spawning an entity and setting its data,
/// so it never observes the entity in a half-initialised state.
/// </summary>
public sealed record BundleDelimiterS2C : IMessage;

/// <summary>
/// "Join Game" — the first play packet. Establishes the player's entity id and the
/// dimension they spawn into. (A minimal subset of the vanilla fields; the full
/// registry codec NBT is future work.)
/// </summary>
public sealed record JoinGameS2C(
    int EntityId,
    byte GameMode,
    string DimensionName,
    long HashedSeed,
    int ViewDistance,
    bool ReducedDebugInfo) : IMessage;

/// <summary>
/// Respawn (0x41): re-initialises the client into a dimension WITHOUT a reconnect — the mechanism for
/// switching a connected player's world. The client unloads its current world; the server then streams
/// the new world's chunks and re-positions the player. <see cref="DimensionType"/> must be a type the
/// registry codec (sent with Join Game) defines.
/// </summary>
public sealed record RespawnS2C(string DimensionType, string WorldName, long HashedSeed, byte GameMode, bool IsFlat) : IMessage;

/// <summary>Server → client liveness probe; the client echoes the id back.</summary>
public sealed record KeepAliveS2C(long Id) : IMessage;

/// <summary>Updates the player's health, food and saturation HUD.</summary>
public sealed record SetHealthS2C(float Health, int Food, float Saturation) : IMessage;

/// <summary>
/// Tells the client which chunk is the centre of its loaded area. Required before
/// streaming chunks, or the client discards them and never leaves the loading screen.
/// </summary>
public sealed record SetCenterChunkS2C(int ChunkX, int ChunkZ) : IMessage;

/// <summary>Sets the world spawn point (compass target / respawn anchor).</summary>
public sealed record SetDefaultSpawnPositionS2C(Vector3i Position, float Angle) : IMessage;

/// <summary>Teleports/positions the player; the client confirms with the teleport id.</summary>
public sealed record SynchronizePlayerPositionS2C(
    double X, double Y, double Z, float Yaw, float Pitch, int TeleportId) : IMessage;

/// <summary>
/// Tells the client a single block changed. Carries OUR <see cref="BlockType"/> plus an optional
/// <see cref="BlockState"/> refinement (<c>null</c> = the block's default state); the codec maps it
/// to the connection's wire block-state id, so colour/facing variants resolve per protocol version.
/// </summary>
public sealed record BlockUpdateS2C(Vector3i Position, BlockType Block, BlockState? State = null) : IMessage;

/// <summary>
/// Acknowledges a dig/place by echoing its sequence number, so the client can
/// reconcile its predicted block change with the server's authoritative result.
/// </summary>
public sealed record AckBlockChangeS2C(int Sequence) : IMessage;

/// <summary>
/// "Chunk Data and Update Light" (0x24). The body — heightmaps, paletted section
/// data, block entities and light — is pre-serialized by <see cref="ChunkSerializer"/>,
/// so the codec just writes the bytes verbatim.
/// </summary>
public sealed record ChunkDataS2C(byte[] Payload) : IMessage;

/// <summary>
/// Sets a dropped-item entity's contents via entity metadata (data index 8 = the
/// item slot). Must follow <see cref="SpawnEntityS2C"/> for an item entity, or the
/// client renders an empty item — and item-tracking client mods can crash on it.
/// Carries OUR <see cref="ItemStack"/>; the codec maps it to a wire Slot.
/// </summary>
public sealed record SetItemEntityMetadataS2C(int EntityId, ItemStack Stack) : IMessage;

/// <summary>
/// Spawns a non-living entity (e.g. a dropped item) on the client. <see cref="Type"/> is OUR
/// entity kind; the codec maps it to the connection's wire entity-type id.
/// </summary>
public sealed record SpawnEntityS2C(
    int EntityId,
    Guid Uuid,
    EntityType Type,
    double X, double Y, double Z,
    byte Pitch, byte Yaw, byte HeadYaw,
    int Data,
    short VelocityX, short VelocityY, short VelocityZ,
    // For a falling_block, the spawn Data is the carried block's state id — which is a per-protocol
    // value, so it can't be baked here. When set, the codec resolves it via the connection's type
    // mapper and writes it as Data (overriding the int above).
    BlockType? BlockData = null) : IMessage;

/// <summary>Set Entity Velocity (0x54): an entity's velocity in units of 1/8000 of a block per tick.
/// The client applies this explicitly — more reliable than the velocity carried by Spawn Entity.</summary>
public sealed record SetEntityVelocityS2C(int EntityId, short VelocityX, short VelocityY, short VelocityZ) : IMessage;

/// <summary>Collect Item (0x67): plays the "item flies into the collector" pickup animation. Purely
/// cosmetic — the item entity is still removed separately (Remove Entities) or its count updated.</summary>
public sealed record CollectItemS2C(int CollectedEntityId, int CollectorEntityId, int PickupItemCount) : IMessage;

// ── Play: serverbound ───────────────────────────────────────────────────────

/// <summary>Client's reply to <see cref="KeepAliveS2C"/>.</summary>
public sealed record KeepAliveC2S(long Id) : IMessage;

/// <summary>The client confirms it applied a teleport (SynchronizePlayerPosition), echoing its id.</summary>
public sealed record ConfirmTeleportationC2S(int TeleportId) : IMessage;

/// <summary>Client reports its new position (no rotation).</summary>
public sealed record SetPlayerPositionC2S(double X, double Y, double Z, bool OnGround) : IMessage;

/// <summary>
/// Block digging. <see cref="Status"/>: 0 started, 1 cancelled, 2 finished. Survival
/// finishes with 2; creative instant-break arrives as 0. <see cref="Face"/> is the
/// clicked block face; <see cref="Sequence"/> acknowledges the change.
/// </summary>
public sealed record PlayerActionC2S(int Status, Vector3i Position, byte Face, int Sequence) : IMessage;

/// <summary>
/// Use an item against a block — i.e. block placement. <see cref="Face"/> is the
/// face clicked (placement goes on the adjacent block in that direction).
/// </summary>
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
