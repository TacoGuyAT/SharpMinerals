using SharpMinerals.Entities;
using SharpMinerals.Entities.Components;
using SharpMinerals.Network;
using SharpMinerals.Network.Messages;
using ArchWorld = Arch.Core.World;
using ArchEntity = Arch.Core.Entity;

namespace SharpMinerals.Level.Systems;

/// <summary>Per-player entity visibility, driven by <see cref="World"/> lifetime + streaming EVENTS rather than a
/// per-tick scan. It holds two column-keyed maps - which entities sit in each chunk column, and which viewers'
/// clients hold each column - and on every transition (entity spawned / despawned / changed column; a viewer
/// loaded / unloaded a column) sends just the spawn or <see cref="RemoveEntitiesS2C"/> delta to the affected
/// clients. Each client's set of currently-spawned ids lives on its <see cref="EntityTrackerComponent"/>. This is
/// the single owner of entity spawn/despawn to clients (players, dropped items, falling blocks). Net ids for loose
/// entities are stamped here on spawn (replacing the old pre-tick Announce), so the physical gates that read
/// <c>EntityId != 0</c> see them immediately. World events fire from both the tick and network threads, so every
/// handler runs under <see cref="gate"/>.</summary>
public sealed class EntityTrackerSystem : ISystem {
    readonly World world;
    readonly object gate = new();

    // The chunk column each tracked entity occupies, plus its network id (so despawn/move need no re-derivation).
    readonly Dictionary<ArchEntity, Tracked> tracked = new();
    // column -> entities currently filed in it (any kind).
    readonly Dictionary<(Mint X, Mint Z), HashSet<ArchEntity>> entitiesByColumn = new();
    // column -> viewers whose client currently holds it.
    readonly Dictionary<(Mint X, Mint Z), HashSet<ArchEntity>> viewersByColumn = new();

    public EntityTrackerSystem(World world) {
        this.world = world;
        world.EntitySpawned += OnEntitySpawned;
        world.EntityDespawning += OnEntityDespawning;
        world.ViewerLoadedColumn += OnViewerLoadedColumn;
        world.ViewerUnloadedColumn += OnViewerUnloadedColumn;
        world.Entities.EntityChangedColumn += OnEntityChangedColumn;
    }

    // -- Entity lifetime -------------------------------------------------------

    void OnEntitySpawned(World w, ArchEntity e) {
        lock (gate) {
            var ecs = w.Ecs;
            int netId = ResolveOrAssignNetId(ecs, e);
            if (netId == 0) return; // not a trackable kind (or an empty drop)
            var column = World.ColumnOf(ecs.Get<TransformEntityComponent>(e));
            Bucket(entitiesByColumn, column).Add(e);
            tracked[e] = new Tracked(column, netId);
            if (viewersByColumn.TryGetValue(column, out var viewers))
                foreach (var viewer in viewers)
                    if (viewer != e) SpawnTo(ecs, viewer, e, netId);
        }
    }

    void OnEntityDespawning(World w, ArchEntity e) {
        lock (gate) {
            var ecs = w.Ecs;
            if (tracked.Remove(e, out var info)) {
                Drop(entitiesByColumn, info.Column, e);
                if (viewersByColumn.TryGetValue(info.Column, out var viewers))
                    foreach (var viewer in viewers)
                        if (viewer != e) RemoveFrom(ecs, viewer, info.NetId);
            }
            // If the despawning entity is itself a viewer (a leaving player), forget it as a viewer too.
            if (ecs.Has<ChunkViewEntityComponent>(e))
                foreach (var column in ecs.Get<ChunkViewEntityComponent>(e).Loaded)
                    Drop(viewersByColumn, column, e);
        }
    }

    void OnEntityChangedColumn(ArchEntity e, (Mint X, Mint Z) oldColumn, (Mint X, Mint Z) newColumn) {
        lock (gate) {
            if (!tracked.TryGetValue(e, out var info)) return; // not tracked (e.g. a non-networked entity)
            var ecs = world.Ecs;
            Drop(entitiesByColumn, oldColumn, e);
            Bucket(entitiesByColumn, newColumn).Add(e);
            tracked[e] = new Tracked(newColumn, info.NetId);

            viewersByColumn.TryGetValue(oldColumn, out var leaving);
            viewersByColumn.TryGetValue(newColumn, out var entering);
            if (leaving is not null)
                foreach (var viewer in leaving)
                    if (viewer != e && (entering is null || !entering.Contains(viewer))) RemoveFrom(ecs, viewer, info.NetId);
            if (entering is not null)
                foreach (var viewer in entering)
                    if (viewer != e && (leaving is null || !leaving.Contains(viewer))) SpawnTo(ecs, viewer, e, info.NetId);
        }
    }

    // -- Viewer view changes ---------------------------------------------------

    void OnViewerLoadedColumn(World w, ArchEntity viewer, (Mint X, Mint Z) column) {
        lock (gate) {
            Bucket(viewersByColumn, column).Add(viewer);
            if (entitiesByColumn.TryGetValue(column, out var entities))
                foreach (var e in entities)
                    if (e != viewer && tracked.TryGetValue(e, out var info)) SpawnTo(w.Ecs, viewer, e, info.NetId);
        }
    }

    void OnViewerUnloadedColumn(World w, ArchEntity viewer, (Mint X, Mint Z) column) {
        lock (gate) {
            Drop(viewersByColumn, column, viewer);
            if (entitiesByColumn.TryGetValue(column, out var entities))
                foreach (var e in entities)
                    if (e != viewer && tracked.TryGetValue(e, out var info)) RemoveFrom(w.Ecs, viewer, info.NetId);
        }
    }

    // -- Send helpers ----------------------------------------------------------

    void SpawnTo(ArchWorld ecs, ArchEntity viewer, ArchEntity entity, int netId) {
        if (ClientOf(ecs, viewer) is not { } client) return; // viewer isn't a live client (yet)
        if (ecs.Get<EntityTrackerComponent>(viewer).Sent.Add(netId)) // first time this client sees it
            SendSpawn(client, ecs, entity);
    }

    void RemoveFrom(ArchWorld ecs, ArchEntity viewer, int netId) {
        if (!ecs.Has<EntityTrackerComponent>(viewer)) return;
        if (ecs.Get<EntityTrackerComponent>(viewer).Sent.Remove(netId) && ClientOf(ecs, viewer) is { } client)
            client.Send(new RemoveEntitiesS2C([netId]));
    }

    // TODO: refactor into INetEntityComponent, ecs.Has<INetEntityComponent>()
    void SendSpawn(NetClient client, ArchWorld ecs, ArchEntity entity) {
        if (ecs.Has<PlayerEntityComponent>(entity))
            PlayerVisibility.SendSpawn(client, ecs, entity);
        else if (ecs.Has<PickupEntityComponent>(entity)) {
            var (d, t, v) = ecs.Get<PickupEntityComponent, TransformEntityComponent, VelocityEntityComponent>(entity);
            ItemLifecycleSystem.SendSpawn(client.Send, d.EntityId, CoreMod.Item, d.Stack, t, v);
        } else if (ecs.Has<FallingBlockEntityComponent>(entity)) {
            var f = ecs.Get<FallingBlockEntityComponent>(entity);
            FallingBlockSystem.SendSpawn(client.Send, f.EntityId, f.Block, ecs.Get<TransformEntityComponent>(entity));
        }
    }

    /// <returns>0 = not a trackable kind.</returns>
    int ResolveOrAssignNetId(ArchWorld ecs, ArchEntity entity) {
        if (ecs.Has<PlayerEntityComponent>(entity))
            return ecs.Get<PlayerEntityComponent>(entity).NetId;
        if (ecs.Has<PickupEntityComponent>(entity)) {
            ref var d = ref ecs.Get<PickupEntityComponent>(entity);
            if (d.Stack.IsEmpty) return 0;
            if (d.EntityId == 0) d.EntityId = world.NextEntityId?.Invoke() ?? 0;
            return d.EntityId;
        }
        if (ecs.Has<FallingBlockEntityComponent>(entity)) {
            ref var f = ref ecs.Get<FallingBlockEntityComponent>(entity);
            if (f.EntityId == 0) f.EntityId = world.NextEntityId?.Invoke() ?? 0;
            return f.EntityId;
        }
        return 0;
    }

    static NetClient? ClientOf(ArchWorld ecs, ArchEntity viewer) =>
        ecs.Has<SenderEntityComponent>(viewer) ? ecs.Get<SenderEntityComponent>(viewer).Client : null;

    static HashSet<ArchEntity> Bucket(Dictionary<(Mint X, Mint Z), HashSet<ArchEntity>> map, (Mint X, Mint Z) column) =>
        map.TryGetValue(column, out var set) ? set : map[column] = new();

    static void Drop(Dictionary<(Mint X, Mint Z), HashSet<ArchEntity>> map, (Mint X, Mint Z) column, ArchEntity e) {
        if (map.TryGetValue(column, out var set) && set.Remove(e) && set.Count == 0) map.Remove(column);
    }

    readonly record struct Tracked((Mint X, Mint Z) Column, int NetId);
}
