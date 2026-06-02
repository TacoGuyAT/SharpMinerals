using SharpMinerals.Entities;
using SharpMinerals.Items;
using SharpMinerals.Math;

namespace SharpMinerals.Network.Messages;

public sealed record PlayerListEntry(Guid Uuid, string Name, int GameMode, bool Listed, int Latency);

// ── Clientbound: player presence & entity sync ───────────────────────────────

/// <summary>
/// Player Info Update (0x3A): registers/updates player profiles for rendering + the tab list.
/// Always sends ADD_PLAYER, UPDATE_GAME_MODE, UPDATE_LISTED, UPDATE_LATENCY.
/// </summary>
public sealed record PlayerInfoUpdateS2C(IReadOnlyList<PlayerListEntry> Entries) : IMessage;

/// <summary>Player Info Remove (0x39).</summary>
public sealed record PlayerInfoRemoveS2C(IReadOnlyList<Guid> Uuids) : IMessage;

/// <summary>
/// Spawns another player's entity, carrying both <see cref="Uuid"/> and <see cref="Name"/>: modern (0x03)
/// keys off the UUID, legacy 1.5.2 (0x14 Spawn Named Entity) carries the name inline.
/// </summary>
public sealed record SpawnPlayerS2C(int EntityId, Guid Uuid, string Name, double X, double Y, double Z, float Yaw, float Pitch) : IMessage;

/// <summary>Teleport Entity (0x68): absolute position+rotation.</summary>
public sealed record TeleportEntityS2C(int EntityId, double X, double Y, double Z, float Yaw, float Pitch, bool OnGround) : IMessage;

/// <summary>Entity Head Rotation (0x42).</summary>
public sealed record EntityHeadRotationS2C(int EntityId, float HeadYaw) : IMessage;

public enum EntityAnimation { SwingMainArm, SwingOffArm }

public sealed record EntityAnimationS2C(int EntityId, EntityAnimation Animation) : IMessage;

/// <summary>Entity Flags: an entity's shared-flags state changed (sneaking, sprinting, …); each protocol
/// maps it to its own entity-metadata form (modern flags byte + Pose; legacy flags byte only).</summary>
public sealed record EntityFlagsS2C(int EntityId, EntityFlags Flags) : IMessage;

/// <summary>An entity's equipment slots, ordered as 1.20.1's Set Equipment indices. The same packet that
/// shows another player's held item also shows their armour — these are its slot ids.</summary>
public enum EquipmentSlot { MainHand = 0, OffHand = 1, Boots = 2, Leggings = 3, Chestplate = 4, Helmet = 5 }

/// <summary>Set Equipment (0x55): the item another entity holds/wears in ONE slot, so other clients
/// render its held item and armour. Carries OUR <see cref="ItemStack"/>; the codec maps it to a wire Slot.
/// One slot per message — modern writes a single-entry array; legacy 1.5.2's 0x05 is one slot per packet.</summary>
public sealed record SetEquipmentS2C(int EntityId, EquipmentSlot Slot, ItemStack Item) : IMessage;

/// <summary>Remove Entities (0x3E).</summary>
public sealed record RemoveEntitiesS2C(IReadOnlyList<int> EntityIds) : IMessage;

// ── Serverbound: movement & interaction ──────────────────────────────────────

/// <summary>Set Player Position and Rotation (0x15).</summary>
public sealed record SetPlayerPositionAndRotationC2S(double X, double Y, double Z, float Yaw, float Pitch, bool OnGround) : IMessage;

/// <summary>Set Player Rotation (0x16).</summary>
public sealed record SetPlayerRotationC2S(float Yaw, float Pitch, bool OnGround) : IMessage;

/// <summary>Interact Entity (0x10): attack or interact with an entity (type 1 = attack).</summary>
public sealed record InteractEntityC2S(int TargetId, int Type, bool Sneaking) : IMessage;

/// <summary>Swing Arm (0x2F).</summary>
public sealed record SwingArmC2S(int Hand) : IMessage;

/// <summary>The kinds of <see cref="EntityActionC2S"/> we model; unrecognised actions decode to <see cref="Other"/>.</summary>
public enum EntityActionKind { StartSneaking, StopSneaking, StartSprinting, StopSprinting, Other }

/// <summary>Entity Action / Player Command (0x1E).</summary>
public sealed record EntityActionC2S(EntityActionKind Action) : IMessage;
