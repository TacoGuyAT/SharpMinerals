using SharpMinerals.Entities.Components;

namespace SharpMinerals.Commands;

/// <summary>A composite command demonstrating subcommands: <c>/server tps|players|stop</c>.</summary>
public sealed class ServerCommand : CompositeCommand {
    public override string Name => "server";
    public override string Description => "Server info and management";

    public ServerCommand() {
        Add(new LambdaCommand("tps", "Show the tick rate", "/server tps", ctx => {
            var server = Server.Instance;
            ctx.Reply($"TPS target {server?.TicksPerSecond ?? 0}, tick {server?.CurrentTick ?? 0}");
            return Task.CompletedTask;
        }));

        Add(new LambdaCommand("players", "List online players", "/server players", ctx => {
            var server = Server.Instance;
            ctx.Reply($"Players online: {server?.PlayerCount ?? 0}");
            if (server is not null)
                foreach (var (clientId, handle) in server.Players)
                    if (handle.World.Ecs.IsAlive(handle.Entity))
                        ctx.Reply($"  {handle.World.Ecs.Get<NetworkedPlayer>(handle.Entity).Name} (#{clientId})");
            return Task.CompletedTask;
        }));

        Add(new LambdaCommand("stop", "Stop the server", "/server stop", ctx => {
            ctx.Reply("stopping");
            Server.Instance?.Stop();
            return Task.CompletedTask;
        }));
    }
}
