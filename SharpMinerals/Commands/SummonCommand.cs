using Brigadier.NET;
using Brigadier.NET.Builder;
using Brigadier.NET.Context;
using SharpMinerals.Entities;
using SharpMinerals.Entities.Components;
using SharpMinerals.Level;

namespace SharpMinerals.Commands;

/// <summary>
/// <c>/summon &lt;entity&gt; [&lt;x&gt; &lt;y&gt; &lt;z&gt;]</c> — spawns an entity of the given kind (by registry
/// name, tab-completed) at the supplied coordinates, or at the issuing player's feet when they're omitted. Carries
/// NO per-entity data yet (no item stack, carried block, or NBT), so data-bearing kinds spawn with their blueprint
/// defaults — data handling is a later addition. Players can't be summoned (they need a connection).
/// </summary>
public static class SummonCommand {
    const double Limit = 3e7; // ~world border; the coordinate bound Brigadier validates against

    public static CommandDispatcher RegisterSummon(this CommandDispatcher d) => d.Register(l => l
        .Literal("summon")
        .Then(x => x.Argument("entity", ResourceLocationArgumentType.ResourceLocation())
            .Suggests((ctx, builder) => {
                foreach (var type in EntityRegistry.All)
                    if (type != EntityRegistry.Player // not summonable
                        && (type.Id.Full.StartsWith(builder.Remaining, StringComparison.OrdinalIgnoreCase)
                            || type.Id.Name.StartsWith(builder.Remaining, StringComparison.OrdinalIgnoreCase)))
                        builder.Suggest(type.Id.Full); // suggest the canonical namespaced id; bare input still resolves
                return builder.BuildFuture();
            })
            // No-coords form spawns at the sender's position, so it needs a player (handled in SummonAtSender).
            .Executes(SummonAtSender)
            .Then(x => x.Argument("x", Arguments.Double(-Limit, Limit))
                .Then(x => x.Argument("y", Arguments.Double(-Limit, Limit))
                    .Then(x => x.Argument("z", Arguments.Double(-Limit, Limit)).Executes(c =>
                        SummonAt(c, Arguments.GetDouble(c, "x"), Arguments.GetDouble(c, "y"), Arguments.GetDouble(c, "z"))))))));

    static int SummonAtSender(CommandContext<SenderContext> ctx) {
        var server = ctx.Source.Server;
        if (ctx.Source.Client is not { } client
            || !server.TryGetPlayer(client.Id, out var context)
            || !context.World.Ecs.IsAlive(context.Entity)) {
            ctx.Source.Reply("Specify coordinates: /summon <entity> <x> <y> <z>.");
            return 0;
        }
        var t = context.World.Ecs.Get<TransformEntityComponent>(context.Entity);
        return Summon(ctx, context.World, t.X, t.Y, t.Z);
    }

    static int SummonAt(CommandContext<SenderContext> ctx, double x, double y, double z) {
        var server = ctx.Source.Server;
        // Spawn into the issuing player's world, or the default world when run from the console.
        var world = ctx.Source.Client is { } client && server.TryGetPlayer(client.Id, out var context)
            ? context.World
            : server.DefaultWorld;
        return Summon(ctx, world, x, y, z);
    }

    static int Summon(CommandContext<SenderContext> ctx, World world, double x, double y, double z) {
        var name = Arguments.GetString(ctx, "entity");
        if (EntityRegistry.FromName(name) is not { } type) {
            ctx.Source.Reply($"Unknown entity '{name}'.");
            return 0;
        }
        if (type == EntityRegistry.Player) {
            ctx.Source.Reply("Players can't be summoned.");
            return 0;
        }

        world.Spawn(type, new TransformEntityComponent(x, y, z));
        ctx.Source.Reply($"Summoned {type.Id.Full} at ({x:0.##}, {y:0.##}, {z:0.##}).");
        return 1;
    }
}
