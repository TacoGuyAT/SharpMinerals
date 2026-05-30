#if TEST_HARNESS
using SharpMinerals.Blocks;
using SharpMinerals.Entities.Components;
using SharpMinerals.Math;
using World = SharpMinerals.Level.World;

namespace SharpMinerals.Commands;

/// <summary>
/// <c>/openchest</c> — opens a freshly-created chest container window for every connected
/// player. A harness aid to exercise the container packets on a real client without
/// needing the bot to place and right-click a chest block.
/// </summary>
public sealed class OpenChestCommand : ICommand {
    public string Name => "openchest";
    public string Description => "Opens a chest container for connected players (test harness)";
    public string Usage => "/openchest";

    public Task ExecuteAsync(CommandContext ctx) {
        var server = Server.Instance;
        if (server is null) return Task.CompletedTask;

        foreach (var (clientId, handle) in server.Players) {
            if (!handle.World.Ecs.IsAlive(handle.Entity)) continue;
            var t = handle.World.Ecs.Get<Transform>(handle.Entity);
            var pos = new Vector3i((int)t.X, (int)t.Y + 2, (int)t.Z);
            var chest = handle.World.GetBlockEntity(pos) ?? Make(handle.World, pos);
            server.Containers.Open(server, clientId, chest);
            ctx.Reply($"opened chest for #{clientId} at {pos}");
        }
        return Task.CompletedTask;
    }

    static BlockEntity Make(World world, Vector3i pos) {
        var entity = new BlockEntity(pos, BlockRegistry.Chest);
        world.SetBlockEntity(entity);
        return entity;
    }
}
#endif
