using Brigadier.NET;
using Brigadier.NET.Builder;
using SharpMinerals.Level;

namespace SharpMinerals.Commands;

/// <summary><c>/world &lt;name&gt;</c> — switches the issuing player to a world, creating it fresh (in-memory
/// superflat) if absent. Player-only. Also the basis for per-test world isolation.</summary>
public static class WorldCommand {
    public static CommandDispatcher RegisterWorld(this CommandDispatcher d) => d.Register(l => l
        .Literal("world")
        .Then(a => a.Argument("name", Arguments.Word()).Requires(s => s.IsPlayer)
            .Suggests((ctx, builder) => {
                foreach (var wname in ctx.Source.Server.Worlds.Keys)
                    if (wname.StartsWith(builder.Remaining, StringComparison.OrdinalIgnoreCase)) builder.Suggest(wname);
                return builder.BuildFuture();
            })
            .Executes(ctx => {
            var server = ctx.Source.Server;
            if (ctx.Source.Client is not { } client) { ctx.Source.Reply("Only a player can switch worlds."); return 0; }
            var world = server.GetOrCreateWorld(Arguments.GetString(ctx, "name"),
                static (name, srv) => new World(name) { Events = srv.Events });
            server.SwitchWorld(client.Id, world);
            ctx.Source.Reply($"Switched to world '{world.Name}'.");
            return 1;
        })));
}
