using SharpMinerals.Math;

namespace SharpMinerals.Persistence;

/// <summary>
/// Wraps any <see cref="IWorldStore"/> with write-behind: chunk saves (already immutable
/// <c>byte[]</c> blobs) are queued and flushed by a background worker, so <c>/save</c> and
/// shutdown don't block on disk. Reads see not-yet-flushed writes; <see cref="Dispose"/> drains.
/// </summary>
public sealed class AsyncWorldStore : IWorldStore, IDisposable {
    readonly IWorldStore inner;
    readonly WriteBehind<(string World, Vector3i Chunk), byte[]> writes;

    public AsyncWorldStore(IWorldStore inner) {
        this.inner = inner;
        writes = new WriteBehind<(string World, Vector3i Chunk), byte[]>(
            (key, data) => inner.SaveChunk(key.World, key.Chunk, data), "chunk");
    }

    public void SaveChunk(string world, Vector3i chunk, byte[] data) => writes.Enqueue((world, chunk), data);

    public bool TryLoadChunk(string world, Vector3i chunk, out byte[] data) =>
        writes.TryPending((world, chunk), out data!) || inner.TryLoadChunk(world, chunk, out data);

    public void Dispose() {
        writes.Dispose();
        (inner as IDisposable)?.Dispose();
    }
}
