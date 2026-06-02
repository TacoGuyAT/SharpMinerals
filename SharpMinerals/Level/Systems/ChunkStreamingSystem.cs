using Arch.Core;
using SharpMinerals.Entities.Components;
using SharpMinerals.Network;
using ArchEntity = Arch.Core.Entity;

namespace SharpMinerals.Level.Systems;

/// <summary>Streams chunk columns to each player as they move. Each tick it re-runs the per-player streamer,
/// whose <see cref="ChunkViewEntityComponent"/> diff gates actual sends to chunk-boundary crossings (so a
/// player standing still streams nothing). The initial view on join is still streamed synchronously by
/// <see cref="Streaming"/> on PlayerJoined — only the on-move re-stream moved here. Replaces the PlayerMoved event.</summary>
public sealed class ChunkStreamingSystem : ITickable, INetworkSystem {
    static readonly QueryDescription PlayerQuery =
        new QueryDescription().WithAll<NetPlayerEntityComponent, ChunkViewEntityComponent>();

    readonly World world;
    public ChunkStreamingSystem(World world) => this.world = world;

    public void Tick() { } // streaming is projection only, in Flush

    public void Flush(Server server) {
        world.Ecs.Query(in PlayerQuery, (ArchEntity e, ref NetPlayerEntityComponent net) => {
            if (server.TryGetPlayer(net.ClientId, out var ctx))
                Streaming.Restream(ctx);
        });
    }
}
