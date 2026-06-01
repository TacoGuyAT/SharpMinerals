using Brigadier.NET;
using Brigadier.NET.Builder;
using SharpMinerals.Level;

namespace SharpMinerals.Commands;

/// <summary><c>/world &lt;name&gt;</c> — switches the issuing player to a world, creating it fresh (in-memory
/// superflat) if it doesn't exist. Player-only (the console has no world to switch). The basis for per-test
/// world isolation: each test names its own throwaway world.</summary>
public static class WorldCommand {
    public static CommandDispatcher RegisterWorld(this CommandDispatcher d) => d.Register(l => l
        .Literal("world")
        .Then(a => a.Argument("name", Arguments.Word()).Requires(s => s.IsPlayer)
            .Suggests((ctx, builder) => {
                if (ctx.Source.Server is { } srv)
                    foreach (var wname in srv.Worlds.Keys)
                        if (wname.StartsWith(builder.Remaining, StringComparison.OrdinalIgnoreCase)) builder.Suggest(wname);
                return builder.BuildFuture();
            })
            .Executes(c => {
            var server = c.Source.Server;
            if (server is null) { c.Source.Reply("Server is not running."); return 0; }
            if (c.Source.Client is not { } client) { c.Source.Reply("Only a player can switch worlds."); return 0; }
            var world = server.GetOrCreateWorld(Arguments.GetString(c, "name"),
                static (name, srv) => new World(name) { Events = srv.Events });
            server.SwitchWorld(client.Id, world);
            c.Source.Reply($"Switched to world '{world.Name}'.");
            return 1;
        })));
}
