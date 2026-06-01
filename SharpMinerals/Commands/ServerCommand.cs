using Brigadier.NET;
using Brigadier.NET.Builder;
using SharpMinerals.Entities.Components;

namespace SharpMinerals.Commands;

/// <summary><c>/server tps|players|stop</c> — server info and management (nested literal subcommands).</summary>
public static class ServerCommand {
    public static CommandDispatcher RegisterServer(this CommandDispatcher d) => d.Register(l => l
        .Literal("server")
        .Then(a => a.Literal("tps").Executes(c => {
            var s = c.Source.Server;
            c.Source.Reply($"TPS target {s?.TicksPerSecond ?? 0}, tick {s?.CurrentTick ?? 0}");
            return 1;
        }))
        .Then(a => a.Literal("players").Executes(c => {
            var s = c.Source.Server;
            c.Source.Reply($"Players online: {s?.PlayerCount ?? 0}");
            if (s is not null)
                foreach (var (clientId, context) in s.Players)
                    if (context.World.Ecs.IsAlive(context.Entity))
                        c.Source.Reply($"  {context.World.Ecs.Get<NetPlayerEntityComponent>(context.Entity).Name} (#{clientId})");
            return 1;
        }))
        .Then(a => a.Literal("stop").Executes(c => {
            c.Source.Reply("stopping");
            c.Source.Server?.Stop();
            return 1;
        }))
        .Executes(c => { c.Source.Reply("/server <tps|players|stop>"); return 1; }));
}
