using SharpMinerals.Math;

namespace SharpMinerals.Persistence;

/// <summary>Wraps a world's <see cref="IWorldStore"/> with write-behind: saves are queued and flushed by a
/// background worker, so <c>/save</c> and shutdown don't block on disk. Reads see not-yet-flushed writes.
/// <see cref="Dispose"/> (reached via <c>World.Unload</c> - the world owns its store) drains the queues,
/// then disposes the inner store.</summary>
public sealed class AsyncWorldStore : IWorldStore, IDisposable {
    // The entity blob is a single value; the write-behind still wants a key, so it gets a constant one.
    const string EntitiesKey = "entities";

    readonly IWorldStore inner;
    readonly WriteBehind<Vector3i, byte[]> writes;
    readonly WriteBehind<string, byte[]> entityWrites;

    public AsyncWorldStore(IWorldStore inner) {
        this.inner = inner;
        writes = new WriteBehind<Vector3i, byte[]>(inner.SaveChunk, "chunk");
        entityWrites = new WriteBehind<string, byte[]>((_, data) => inner.SaveEntities(data), "entities");
    }

    public void SaveChunk(Vector3i chunk, byte[] data) => writes.Enqueue(chunk, data);

    public bool TryLoadChunk(Vector3i chunk, out byte[] data) =>
        writes.TryPending(chunk, out data!) || inner.TryLoadChunk(chunk, out data);

    public void SaveEntities(byte[] data) => entityWrites.Enqueue(EntitiesKey, data);

    public byte[]? LoadEntities() =>
        entityWrites.TryPending(EntitiesKey, out var pending) ? pending : inner.LoadEntities();

    public void Dispose() {
        writes.Dispose();
        entityWrites.Dispose();
        (inner as IDisposable)?.Dispose();
    }
}
