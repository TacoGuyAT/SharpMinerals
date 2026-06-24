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
        if (!view.Loaded.Add((columnX, columnZ))) { 
            return false; // the client already has this column
        } 
        context.Client.Send(context.Client.Protocol.BuildChunk(context.World, (int)columnX, (int)columnZ));
        // Terrain is on the wire; let the entity tracker spawn whatever already sits in this column to this viewer.
        context.World.RaiseViewerLoadedColumn(context.Entity, (columnX, columnZ));
        return true;
    }

    /// <summary>Tells the client which chunk its view is centred on; must precede chunk data or off-grid
    /// columns are discarded client-side.</summary>
    static void Recenter(PlayerContext context, Mint cx, Mint cz) {
        if(context.Client.Protocol.ChunkViewCenter((int)cx, (int)cz) is IMessage center) {
            context.Client.Send(center);
        }
    }

    static async void SendColumnWhenReady(PlayerContext context, Mint columnX, Mint columnZ) {
        Chunk[] chunks;
        try {
            chunks = await context.World.GetColumnAsync((int)columnX, (int)columnZ);
        } catch(Exception ex) {
            Log.LogWarning(ex, "column load failed for ({X},{Z})", columnX, columnZ);
            return;
        }
        await context.Server.DeferAsync(() => {
            var ecs = context.World.Ecs;
            if(!ecs.IsAlive(context.Entity)) {
                return; // disconnected, or moved worlds since the request
            }
            var view = ecs.Get<ChunkViewEntityComponent>(context.Entity);
            if(System.Math.Abs(columnX - view.CenterX) > ViewRadius || System.Math.Abs(columnZ - view.CenterZ) > ViewRadius) {
                return; // view moved on - column no longer wanted
            }
            StreamColumn(context, view, columnX, columnZ); // de-dupes via view.Loaded.Add
        });
    }

    static void Stream(PlayerContext context, bool initial) {
        var ecs = context.World.Ecs;
        if(!ecs.IsAlive(context.Entity))
            return;
        var view = ecs.Get<ChunkViewEntityComponent>(context.Entity);
        var transform = ecs.Get<TransformEntityComponent>(context.Entity);

        Mint cx = (Mint)System.Math.Floor(transform.X) >> Chunk.Shifts;
        Mint cz = (Mint)System.Math.Floor(transform.Z) >> Chunk.Shifts;
        bool moved = !view.Initialized || cx != view.CenterX || cz != view.CenterZ;
        
        if(!initial && !moved) {
            return; // stationary - in-flight loads self-deliver, nothing else to do
        }
        
        view.CenterX = cx;
        view.CenterZ = cz;
        view.Initialized = true;
        Recenter(context, cx, cz);

        for(Mint dx = -ViewRadius; dx <= ViewRadius; dx++)
            for(Mint dz = -ViewRadius; dz <= ViewRadius; dz++) {
                var col = (cx + dx, cz + dz);
                if(!view.Loaded.Contains(col)) {
                    SendColumnWhenReady(context, col.Item1, col.Item2);
                }
            }

        // Pure prefetch ring beyond the sent view - no waiter, just kicks generation off early.
        for(Mint dx = -(ViewRadius + RequestMargin); dx <= ViewRadius + RequestMargin; dx++)
            for(Mint dz = -(ViewRadius + RequestMargin); dz <= ViewRadius + RequestMargin; dz++)
                if(System.Math.Abs(dx) > ViewRadius || System.Math.Abs(dz) > ViewRadius)
                    context.World.RequestColumn((int)(cx + dx), (int)(cz + dz));

        view.Loaded.RemoveWhere(c => {
            if(System.Math.Abs(c.X - cx) <= ViewRadius && System.Math.Abs(c.Z - cz) <= ViewRadius)
                return false;
            context.World.RaiseViewerUnloadedColumn(context.Entity, c);
            return true;
        });
    }
}
