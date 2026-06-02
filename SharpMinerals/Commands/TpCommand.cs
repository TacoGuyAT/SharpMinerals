using Brigadier.NET;
using Brigadier.NET.Builder;
using Brigadier.NET.Context;
using SharpMinerals.Entities.Components;

namespace SharpMinerals.Commands;

/// <summary>
/// Teleports a player: <c>/tp &lt;x&gt; &lt;y&gt; &lt;z&gt;</c> (the issuing player — players only) or
/// <c>/tp &lt;player&gt; &lt;x&gt; &lt;y&gt; &lt;z&gt;</c> (a named player, e.g. from the console). The two
/// forms are sibling branches off <c>tp</c>; the self-form is gated to player sources with <c>.Requires</c>.
/// </summary>
public static class TpCommand {
    const double Limit = 3e7; // ~world border, and the bound Brigadier validates coordinates against

    public static CommandDispatcher RegisterTp(this CommandDispatcher d) => d.Register(l => l
        .Literal("tp")
        .Then(x => x.Argument("x", Arguments.Double(-Limit, Limit)).Requires(s => s.IsPlayer)
            .Then(x => x.Argument("y", Arguments.Double(-Limit, Limit))
                .Then(x => x.Argument("z", Arguments.Double(-Limit, Limit)).Executes(c =>
                    c.Source.Client is { } client ? Teleport(c, client.Id) : 0))))
        .Then(ctx => ctx.Argument("player", Arguments.Word())
            .Suggests((ctx, builder) => {
                foreach (var (_, context) in ctx.Source.Server.Players)
                    if (context.World.Ecs.IsAlive(context.Entity)) {
                        var pname = context.World.Ecs.Get<NetPlayerEntityComponent>(context.Entity).Name;
                        if (pname.StartsWith(builder.Remaining, StringComparison.OrdinalIgnoreCase)) builder.Suggest(pname);
                    }
                return builder.BuildFuture();
            })
            .Then(x => x.Argument("x", Arguments.Double(-Limit, Limit))
                .Then(x => x.Argument("y", Arguments.Double(-Limit, Limit))
                    .Then(x => x.Argument("z", Arguments.Double(-Limit, Limit)).Executes(ctx => {
                        var server = ctx.Source.Server;
                        var name = Arguments.GetString(ctx, "player");
                        if (FindPlayer(server, name) is not { } cid) {
                            ctx.Source.Reply($"No online player named '{name}'.");
                            return 0;
                        }
                        return Teleport(ctx, cid);
                    }))))));

    static int Teleport(CommandContext<SenderContext> ctx, ulong target) {
        var server = ctx.Source.Server;
        if (!server.TryGetPlayer(target, out var context) || !context.World.Ecs.IsAlive(context.Entity)) {
            ctx.Source.Reply("Target is not online.");
            return 0;
        }
        double x = Arguments.GetDouble(ctx, "x"), y = Arguments.GetDouble(ctx, "y"), z = Arguments.GetDouble(ctx, "z");
        var t = context.World.Ecs.Get<TransformEntityComponent>(context.Entity);
        server.TeleportPlayer(target, x, y, z, t.Yaw, t.Pitch);
        ctx.Source.Reply($"Teleported to ({x:0.##}, {y:0.##}, {z:0.##}).");
        return 1;
    }

    static ulong? FindPlayer(Server server, string name) {
        foreach (var (clientId, context) in server.Players)
            if (context.World.Ecs.IsAlive(context.Entity) &&
                context.World.Ecs.Get<NetPlayerEntityComponent>(context.Entity).Name == name)
                return clientId;
        return null;
    }
}
