using Brigadier.NET.Builder;
using SharpMinerals.Entities.Components;

namespace SharpMinerals.Commands;

/// <summary><c>/server tps|players|stop</c> — server info and management (nested literal subcommands).</summary>
public static class ServerCommand {
    public static CommandDispatcher RegisterServer(this CommandDispatcher d) => d.Register(l => l
        .Literal("server")
        .Then(x => x.Literal("tps").Executes(ctx => {
            var server = ctx.Source.Server;
            ctx.Source.Reply($"TPS target {server?.TicksPerSecond ?? 0}, tick {server?.CurrentTick ?? 0}");
            return 1;
        }))
        .Then(x => x.Literal("players").Executes(ctx => {
            var server = ctx.Source.Server;
            ctx.Source.Reply($"Players online: {server?.PlayerCount ?? 0}");
            foreach (var (clientId, context) in server.Players)
                if (context.World.Ecs.IsAlive(context.Entity))
                    ctx.Source.Reply($"  {context.World.Ecs.Get<NetPlayerEntityComponent>(context.Entity).Name} (#{clientId})");
            return 1;
        }))
        .Then(x => x.Literal("stop").Executes(ctx => {
            ctx.Source.Reply("Stopping server...");
            ctx.Source.Server.Stop();
            return 1;
        }))
        .Executes(c => { c.Source.Reply("/server <tps|players|stop>"); return 1; }));
}
