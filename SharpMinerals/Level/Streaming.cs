using Microsoft.Extensions.Logging;
using SharpMinerals.Entities.Components;
using SharpMinerals.Events;
using SharpMinerals.Events.Contexts;
using SharpMinerals.Network;

namespace SharpMinerals.Level;

/// <summary>
/// Streams chunk columns to each player: an initial view on join, then new columns as they cross
/// chunk boundaries (forgetting out-of-range ones so they re-send on return). Driven by lifecycle events.
/// </summary>
public static class Streaming {
    static readonly ILogger Log = Logging.For("Net.Chunks");

    /// <summary>Max column radius (eviction keep-set reference); per-client radius is <see cref="Protocol.ChunkViewRadius"/>.</summary>
    public const int ViewRadius = 5;

    public static void Register(EventBus events) {
        events.Subscribe<PlayerJoined>(OnJoin);
    }

    static void OnJoin(PlayerJoined e) => Stream(e.Context, initial: true);

    /// <summary>Streams a player's initial chunk view on demand (join, or after a world switch's Respawn).</summary>
    public static void StreamInitial(PlayerContext context) => Stream(context, initial: true);

    /// <summary>Re-streams as a player moves (no-op until they cross into a new column). Driven each tick by
    /// <see cref="Systems.ChunkStreamingSystem"/>.</summary>
    public static void Restream(PlayerContext context) => Stream(context, initial: false);

    /// <summary>Streams just the column the player stands in (recentering first), so the client can place the
    /// player on loaded terrain immediately — its "Loading terrain" gate releases once it has this chunk + the
    /// position. The surrounding columns follow via <see cref="StreamInitial"/>, which skips this one.</summary>
    public static void StreamSpawnColumn(PlayerContext context) {
        var ecs = context.World.Ecs;
        if (!ecs.IsAlive(context.Entity)) return;
        var view = ecs.Get<ChunkViewEntityComponent>(context.Entity);
        var transform = ecs.Get<TransformEntityComponent>(context.Entity);

        long cx = (long)System.Math.Floor(transform.X / Chunk.Size);
        long cz = (long)System.Math.Floor(transform.Z / Chunk.Size);
        view.CenterX = cx;
        view.CenterZ = cz;
        view.Initialized = true;

        var protocol = context.Client.Protocol;
        if (protocol.ChunkViewCenter((int)cx, (int)cz) is IMessage center)
            context.Client.Send(center); // must precede the chunk data, or the client discards off-grid columns
        if (view.Loaded.Add((cx, cz)))
            context.Client.Send(protocol.BuildChunk(context.World, (int)cx, (int)cz));
    }

    /// <summary>
    /// Streams the columns around a player. The wire format is resolved by the protocol, not here.
    /// </summary>
    static void Stream(PlayerContext context, bool initial) {
        var ecs = context.World.Ecs;
        var client = context.Client;
        if (!ecs.IsAlive(context.Entity))
            return;
        var view = ecs.Get<ChunkViewEntityComponent>(context.Entity);
        var transform = ecs.Get<TransformEntityComponent>(context.Entity);

        long cx = (long)System.Math.Floor(transform.X / Chunk.Size);
        long cz = (long)System.Math.Floor(transform.Z / Chunk.Size);
        if (!initial && view.Initialized && cx == view.CenterX && cz == view.CenterZ)
            return; // still in the same column

        view.CenterX = cx;
        view.CenterZ = cz;
        view.Initialized = true;

        var protocol = client.Protocol;
        int radius = protocol.ChunkViewRadius;
        if (protocol.ChunkViewCenter((int)cx, (int)cz) is IMessage center)
            client.Send(center); // must precede the chunk data, or the client discards off-grid columns

        int sent = 0;
        for (long dx = -radius; dx <= radius; dx++)
            for (long dz = -radius; dz <= radius; dz++) {
                long colX = cx + dx, colZ = cz + dz;
                if (view.Loaded.Add((colX, colZ))) {
                    client.Send(protocol.BuildChunk(context.World, (int)colX, (int)colZ));
                    sent++;
                }
            }

        // Forget columns now outside the view so they re-send if the player returns.
        view.Loaded.RemoveWhere(c =>
            System.Math.Abs(c.X - cx) > radius || System.Math.Abs(c.Z - cz) > radius);

        if (sent > 0)
            Log.LogDebug("Streamed {Count} column(s) to #{Client} around chunk ({X},{Z})", sent, client.Id, cx, cz);
    }
}
