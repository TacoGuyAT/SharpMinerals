using Brigadier.NET;
using Brigadier.NET.Builder;
using SharpMinerals.Entities.Components;
using BrigContext = Brigadier.NET.Context.CommandContext<SharpMinerals.Commands.CommandContext>;

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
        // /tp <x> <y> <z> — the issuing player
        .Then(a => a.Argument("x", Arguments.Double(-Limit, Limit)).Requires(s => s.IsPlayer)
            .Then(b => b.Argument("y", Arguments.Double(-Limit, Limit))
                .Then(z => z.Argument("z", Arguments.Double(-Limit, Limit)).Executes(c =>
                    c.Source.Client is { } client ? Teleport(c, client.Id) : 0))))
        // /tp <player> <x> <y> <z>
        .Then(a => a.Argument("player", Arguments.Word())
            .Suggests((ctx, builder) => {
                if (ctx.Source.Server is { } srv)
                    foreach (var (_, context) in srv.Players)
                        if (context.World.Ecs.IsAlive(context.Entity)) {
                            var pname = context.World.Ecs.Get<NetPlayerEntityComponent>(context.Entity).Name;
                            if (pname.StartsWith(builder.Remaining, StringComparison.OrdinalIgnoreCase)) builder.Suggest(pname);
                        }
                return builder.BuildFuture();
            })
            .Then(b => b.Argument("x", Arguments.Double(-Limit, Limit))
                .Then(y => y.Argument("y", Arguments.Double(-Limit, Limit))
                    .Then(z => z.Argument("z", Arguments.Double(-Limit, Limit)).Executes(c => {
                        var server = c.Source.Server;
                        if (server is null) { c.Source.Reply("Server is not running."); return 0; }
                        var name = Arguments.GetString(c, "player");
                        if (FindPlayer(server, name) is not { } cid) {
                            c.Source.Reply($"No online player named '{name}'.");
                            return 0;
                        }
                        return Teleport(c, cid);
                    }))))));

    static int Teleport(BrigContext c, ulong target) {
        var server = c.Source.Server;
        if (server is null) { c.Source.Reply("Server is not running."); return 0; }
        if (!server.TryGetPlayer(target, out var context) || !context.World.Ecs.IsAlive(context.Entity)) {
            c.Source.Reply("Target is not online.");
            return 0;
        }
        double x = Arguments.GetDouble(c, "x"), y = Arguments.GetDouble(c, "y"), z = Arguments.GetDouble(c, "z");
        // Keep the player's current facing.
        var t = context.World.Ecs.Get<TransformEntityComponent>(context.Entity);
        server.TeleportPlayer(target, x, y, z, t.Yaw, t.Pitch);
        c.Source.Reply($"Teleported to ({x:0.##}, {y:0.##}, {z:0.##}).");
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
