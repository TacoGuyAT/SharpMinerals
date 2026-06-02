using SharpMinerals.Entities.Components;
using SharpMinerals.Level;
using SharpMinerals.Network;
using ArchEntity = Arch.Core.Entity;

namespace SharpMinerals.Events.Contexts;

/// <summary>An <see cref="EntityContext"/> for a player entity, adding the network <see cref="Client"/>
/// connection. Carried by player events; <see cref="Player"/> reads the live component from the world.</summary>
public sealed record PlayerContext(Server Server, World World, ArchEntity Entity, NetClient Client)
    : EntityContext(Server, World, Entity) {
    public NetPlayerEntityComponent Player => World.Ecs.Get<NetPlayerEntityComponent>(Entity);
}
