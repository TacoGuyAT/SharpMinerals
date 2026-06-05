using Arch.Core;
using SharpMinerals.Blocks;
using SharpMinerals.Entities;
using SharpMinerals.Entities.Components;
using SharpMinerals.Events;
using SharpMinerals.Items;
using SharpMinerals.Level.Systems;

namespace SharpMinerals.Network;

/// <summary>Sends the loose world entities (dropped items, falling blocks) that ALREADY EXIST to a joining player,
/// so a client connecting AFTER they were spawned/loaded still sees them. The per-tick item/falling-block systems
/// announce only NEW (id 0) entities and broadcast once - so a late joiner, and EVERY client after a world reload
/// (where loose entities are announced before anyone is connected), would otherwise never receive them. The player
/// analogue is <see cref="PlayerVisibility"/>.</summary>
public static class EntityVisibility {
    static readonly QueryDescription ItemQuery =
        new QueryDescription().WithAll<PickupEntityComponent, TransformEntityComponent, VelocityEntityComponent>();
    static readonly QueryDescription FallingQuery =
        new QueryDescription().WithAll<FallingBlockEntityComponent, TransformEntityComponent>();

    public static void Register(EventBus events) =>
        events.Subscribe<PlayerJoined>(e => OnJoin(e.Context));

    static void OnJoin(Events.Contexts.PlayerContext ctx) {
        var ecs = ctx.World.Ecs;

        // Existing dropped items (already announced -> have a net id; an id-0 drop is announced to everyone,
        // incl. the newcomer, by the next per-tick Announce).
        var items = new List<(int Id, ItemStack Stack, TransformEntityComponent Pos, VelocityEntityComponent Vel)>();
        ecs.Query(in ItemQuery, (ref PickupEntityComponent d, ref TransformEntityComponent t, ref VelocityEntityComponent v) => {
            if (d.EntityId != 0 && !d.Stack.IsEmpty) items.Add((d.EntityId, d.Stack, t, v));
        });
        foreach (var (id, stack, pos, vel) in items)
            ItemLifecycleSystem.SendSpawn(ctx.Client.Send, id, EntityRegistry.Item, stack, pos, vel);

        // Existing falling blocks.
        var falling = new List<(int Id, BlockType Block, TransformEntityComponent Pos)>();
        ecs.Query(in FallingQuery, (ref FallingBlockEntityComponent f, ref TransformEntityComponent t) => {
            if (f.EntityId != 0) falling.Add((f.EntityId, f.Block, t));
        });
        foreach (var (id, block, pos) in falling)
            FallingBlockSystem.SendSpawn(ctx.Client.Send, id, block, pos);
    }
}
