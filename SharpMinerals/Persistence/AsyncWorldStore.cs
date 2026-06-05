using SharpMinerals.Math;

namespace SharpMinerals.Persistence;

/// <summary>Wraps any <see cref="IWorldStore"/> with write-behind: chunk saves are queued and flushed by a
/// background worker, so <c>/save</c> and shutdown don't block on disk. Reads see not-yet-flushed writes.</summary>
public sealed class AsyncWorldStore : IWorldStore, IDisposable {
    readonly IWorldStore inner;
    readonly WriteBehind<(string World, Vector3i Chunk), byte[]> writes;
    readonly WriteBehind<string, byte[]> entityWrites;

    public AsyncWorldStore(IWorldStore inner) {
        this.inner = inner;
        writes = new WriteBehind<(string World, Vector3i Chunk), byte[]>(
            (key, data) => inner.SaveChunk(key.World, key.Chunk, data), "chunk");
        entityWrites = new WriteBehind<string, byte[]>(inner.SaveWorldEntities, "entities");
    }

    public void SaveChunk(string world, Vector3i chunk, byte[] data) => writes.Enqueue((world, chunk), data);

    public bool TryLoadChunk(string world, Vector3i chunk, out byte[] data) =>
        writes.TryPending((world, chunk), out data!) || inner.TryLoadChunk(world, chunk, out data);

    public void SaveWorldEntities(string world, byte[] data) => entityWrites.Enqueue(world, data);

    public byte[]? LoadWorldEntities(string world) =>
        entityWrites.TryPending(world, out var pending) ? pending : inner.LoadWorldEntities(world);

    public void Dispose() {
        writes.Dispose();
        entityWrites.Dispose();
        (inner as IDisposable)?.Dispose();
    }
}
