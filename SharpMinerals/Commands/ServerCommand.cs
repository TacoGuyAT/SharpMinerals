using Brigadier.NET.Builder;
using SharpMinerals.Entities.Components;

namespace SharpMinerals.Commands;

/// <summary><c>/server tps|players|stop</c> - server info and management (nested literal subcommands).</summary>
public static class ServerCommand {
    public static CommandDispatcher RegisterServer(this CommandDispatcher d) => d.Register(l => l
        .Literal("server")
        .Then(x => x.Literal("tps").Executes(ctx => {
            var server = ctx.Source.Server;
            ctx.Source.Reply($"TPS target {server.TicksPerSecond:0.#}, tick {server.CurrentTick}");
            ctx.Source.Reply($"  measured: {server.MeasuredTps(300):0.0} (5m), {server.MeasuredTps(60):0.0} (1m), {server.MeasuredTps(10):0.0} (10s)");
            return 1;
        }))
        .Then(x => x.Literal("players").Executes(ctx => {
            var server = ctx.Source.Server;
            ctx.Source.Reply($"Players online: {server.PlayerCount}");
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
