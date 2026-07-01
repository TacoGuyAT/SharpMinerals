using Arch.Core;
using SharpMinerals.Entities.Components;
using SharpMinerals.Network;
using ArchEntity = Arch.Core.Entity;

namespace SharpMinerals.Level.Systems;

/// <summary>Keeps other clients' view of each player's worn/held equipment in sync. Each tick it re-diffs every
/// player's equipment against what was last broadcast (<see cref="EquipmentEntityComponent.LastSent"/>) and
/// sends only the changed slots - so an inventory mutation needs no announcement; the diff notices. Replaces
/// the old PlayerInventoryChanged event.</summary>
public sealed class EquipmentVisibilitySystem : INetworkSystem {
    static readonly QueryDescription PlayerQuery =
        new QueryDescription().WithAll<PlayerEntityComponent, InventoryEntityComponent, EquipmentEntityComponent>();

    readonly World world;
    public EquipmentVisibilitySystem(World world) => this.world = world;

    public void Flush(Server server) {
        world.Ecs.Query(in PlayerQuery, (ArchEntity e, ref PlayerEntityComponent net) => {
            if (server.TryGetPlayer(net.ClientId, out var ctx))
                PlayerVisibility.OnInventoryChanged(ctx);
        });
    }
}
