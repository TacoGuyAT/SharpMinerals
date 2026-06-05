namespace SharpMinerals.Persistence;

/// <summary>Wraps any <see cref="IEntityStore"/> with write-behind: <see cref="Save"/> returns immediately so
/// disconnects / <c>/save</c> never block on disk. Reads see not-yet-flushed writes. <see cref="Dispose"/> drains.</summary>
public sealed class AsyncEntityStore : IEntityStore, IDisposable {
    readonly IEntityStore inner;
    readonly WriteBehind<Guid, byte[]> writes;

    public AsyncEntityStore(IEntityStore inner) {
        this.inner = inner;
        writes = new WriteBehind<Guid, byte[]>(inner.Save, "entity");
    }

    public void Save(Guid id, byte[] data) => writes.Enqueue(id, data);

    public bool TryLoad(Guid id, out byte[] data) =>
        writes.TryPending(id, out data!) || inner.TryLoad(id, out data);

    public void Dispose() {
        writes.Dispose();                 // drain queued writes first
        (inner as IDisposable)?.Dispose();
    }
}
