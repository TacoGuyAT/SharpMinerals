using SharpMinerals.Math;

namespace SharpMinerals.Network.Messages;

/// <summary>One entry in a <see cref="PlayerInfoUpdateS2C"/> (a tab-list/profile record).</summary>
public sealed record PlayerListEntry(Guid Uuid, string Name, int GameMode, bool Listed, int Latency);

// ── Clientbound: player presence & entity sync ───────────────────────────────

/// <summary>
/// Player Info Update (0x3A): registers/updates player profiles so the client can
/// render them and show them in the tab list. We always send the ADD_PLAYER,
/// UPDATE_GAME_MODE, UPDATE_LISTED and UPDATE_LATENCY actions.
/// </summary>
public sealed record PlayerInfoUpdateS2C(IReadOnlyList<PlayerListEntry> Entries) : IMessage;

/// <summary>Player Info Remove (0x39): drops players from the tab list when they leave.</summary>
public sealed record PlayerInfoRemoveS2C(IReadOnlyList<Guid> Uuids) : IMessage;

/// <summary>Spawn Player (0x03): creates another player's entity in the world.</summary>
public sealed record SpawnPlayerS2C(int EntityId, Guid Uuid, double X, double Y, double Z, float Yaw, float Pitch) : IMessage;

/// <summary>Teleport Entity (0x68): absolute position+rotation update for an entity.</summary>
public sealed record TeleportEntityS2C(int EntityId, double X, double Y, double Z, float Yaw, float Pitch, bool OnGround) : IMessage;

/// <summary>Entity Head Rotation (0x42): where an entity's head is facing.</summary>
public sealed record EntityHeadRotationS2C(int EntityId, float HeadYaw) : IMessage;

/// <summary>Remove Entities (0x3E): despawns entities by id.</summary>
public sealed record RemoveEntitiesS2C(IReadOnlyList<int> EntityIds) : IMessage;

// ── Serverbound: movement & interaction ──────────────────────────────────────

/// <summary>Set Player Position and Rotation (0x15).</summary>
public sealed record SetPlayerPositionAndRotationC2S(double X, double Y, double Z, float Yaw, float Pitch, bool OnGround) : IMessage;

/// <summary>Set Player Rotation (0x16).</summary>
public sealed record SetPlayerRotationC2S(float Yaw, float Pitch, bool OnGround) : IMessage;

/// <summary>Interact Entity (0x10): attack or interact with an entity (type 1 = attack).</summary>
public sealed record InteractEntityC2S(int TargetId, int Type, bool Sneaking) : IMessage;
