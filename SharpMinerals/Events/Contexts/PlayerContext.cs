using SharpMinerals.Entities.Components;
using SharpMinerals.Level;
using SharpMinerals.Network;
using ArchEntity = Arch.Core.Entity;

namespace SharpMinerals.Events.Contexts;

/// <summary>
/// An <see cref="EntityContext"/> for a player entity — adds the network <see cref="Client"/> connection
/// on top of the server/world/entity. Carried by player events; <see cref="Player"/> reads the live
/// <see cref="NetPlayerEntityComponent"/> component from the world.
/// </summary>
public sealed record PlayerContext(Server Server, World World, ArchEntity Entity, NetClient Client)
    : EntityContext(Server, World, Entity) {
    public NetPlayerEntityComponent Player => World.Ecs.Get<NetPlayerEntityComponent>(Entity);
}
