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

    /// <summary>Extra ring requested (not sent) beyond the view, so columns finish generating in the background
    /// before the player crosses into them. Kept within the eviction margin so read-ahead doesn't churn.</summary>
    public const int RequestMargin = 2;

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
    /// player on loaded terrain immediately - its "Loading terrain" gate releases once it has this chunk + the
    /// position. The surrounding columns follow via <see cref="StreamInitial"/>, which skips this one.</summary>
    public static void StreamSpawnColumn(PlayerContext context) {
        var ecs = context.World.Ecs;
        if (!ecs.IsAlive(context.Entity)) return;
        var view = ecs.Get<ChunkViewEntityComponent>(context.Entity);
        var transform = ecs.Get<TransformEntityComponent>(context.Entity);

        Mint cx = (Mint)System.Math.Floor(transform.X) >> Chunk.Shifts;
        Mint cz = (Mint)System.Math.Floor(transform.Z) >> Chunk.Shifts;
        view.CenterX = cx;
        view.CenterZ = cz;
        view.Initialized = true;

        Recenter(context, cx, cz);
        StreamColumn(context, view, cx, cz);
    }

    /// <summary>Sends a single chunk column to the player, unless they already have it loaded; returns whether
    /// it was sent. The wire format is the protocol's. Mods can call this to push a specific column to a player
    /// (e.g. a custom view distance, or refreshing an edited far column).</summary>
    public static bool StreamColumn(PlayerContext context, Mint columnX, Mint columnZ) =>
        context.World.Ecs.IsAlive(context.Entity)
        && StreamColumn(context, context.World.Ecs.Get<ChunkViewEntityComponent>(context.Entity), columnX, columnZ);

    static bool StreamColumn(PlayerContext context, ChunkViewEntityComponent view, Mint columnX, Mint columnZ) {
        if (!view.Loaded.Add((columnX, columnZ))) return false; // the client already has this column
        context.Client.Send(context.Client.Protocol.BuildChunk(context.World, (int)columnX, (int)columnZ));
        // Terrain is on the wire; let the entity tracker spawn whatever already sits in this column to this viewer.
        context.World.RaiseViewerLoadedColumn(context.Entity, (columnX, columnZ));
        return true;
    }

    /// <summary>Tells the client which chunk its view is centred on; must precede chunk data or off-grid
    /// columns are discarded client-side.</summary>
    static void Recenter(PlayerContext context, Mint cx, Mint cz) {
        if (context.Client.Protocol.ChunkViewCenter((int)cx, (int)cz) is IMessage center)
            context.Client.Send(center);
    }

    /// <summary>Streams the columns around a player. The wire format is resolved by the protocol, not here.
    /// Columns are sent only once their full height has finished loading in the background (<see cref="ChunkLoader"/>);
    /// a column still generating is left for a later tick, so generation never blocks the tick thread. Re-run each
    /// tick by <see cref="Systems.ChunkStreamingSystem"/>; a stationary player whose whole view is sent does nothing.</summary>
    static void Stream(PlayerContext context, bool initial) {
        var ecs = context.World.Ecs;
        if (!ecs.IsAlive(context.Entity))
            return;
        var view = ecs.Get<ChunkViewEntityComponent>(context.Entity);
        var transform = ecs.Get<TransformEntityComponent>(context.Entity);

        Mint cx = (Mint)System.Math.Floor(transform.X) >> Chunk.Shifts;
        Mint cz = (Mint)System.Math.Floor(transform.Z) >> Chunk.Shifts;
        bool moved = !view.Initialized || cx != view.CenterX || cz != view.CenterZ;
        if (!initial && !moved && view.Settled)
            return; // stationary and the whole view is already streamed - nothing to do

        if (initial || moved) {
            view.CenterX = cx;
            view.CenterZ = cz;
            view.Initialized = true;
            Recenter(context, cx, cz);
            // Load the whole view + margin ahead in the background, so columns are ready as the player advances.
            for (Mint dx = -(ViewRadius + RequestMargin); dx <= ViewRadius + RequestMargin; dx++)
                for (Mint dz = -(ViewRadius + RequestMargin); dz <= ViewRadius + RequestMargin; dz++)
                    context.World.RequestColumn((int)(cx + dx), (int)(cz + dz));
        }

        // Send any in-view column whose full height has finished loading; keep the rest queued and retry next tick.
        int sent = 0, owed = 0;
        for (Mint dx = -ViewRadius; dx <= ViewRadius; dx++)
            for (Mint dz = -ViewRadius; dz <= ViewRadius; dz++) {
                var column = (cx + dx, cz + dz);
                if (view.Loaded.Contains(column)) continue;
                if (context.World.IsColumnLoaded((int)column.Item1, (int)column.Item2)) {
                    if (StreamColumn(context, view, column.Item1, column.Item2)) sent++;
                } else {
                    context.World.RequestColumn((int)column.Item1, (int)column.Item2); // still generating - keep it queued
                    owed++;
                }
            }
        view.Settled = owed == 0;

        if (initial || moved)
            // Forget columns now outside the view so they re-send if the player returns; the tracker despawns the
            // entities in each dropped column from this viewer.
            view.Loaded.RemoveWhere(c => {
                if (System.Math.Abs(c.X - cx) <= ViewRadius && System.Math.Abs(c.Z - cz) <= ViewRadius) return false;
                context.World.RaiseViewerUnloadedColumn(context.Entity, c);
                return true;
            });

        if (sent > 0)
            Log.LogDebug("Streamed {Count} column(s) to #{Client} around chunk ({X},{Z})", sent, context.Client.Id, cx, cz);
    }
}
