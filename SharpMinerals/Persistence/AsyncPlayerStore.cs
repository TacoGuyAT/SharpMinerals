using SharpMinerals.Entities;

namespace SharpMinerals.Persistence;

/// <summary>
/// Wraps any <see cref="IPlayerStore"/> with write-behind: <see cref="Save"/> hands off the
/// snapshot and returns immediately, so disconnects / <c>/save</c> never block on disk. Reads see
/// not-yet-flushed writes. <see cref="Dispose"/> drains the queue, then disposes the inner store.
/// </summary>
public sealed class AsyncPlayerStore : IPlayerStore, IDisposable {
    readonly IPlayerStore inner;
    readonly WriteBehind<Guid, PlayerState> writes;

    public AsyncPlayerStore(IPlayerStore inner) {
        this.inner = inner;
        writes = new WriteBehind<Guid, PlayerState>(inner.Save, "player");
    }

    public void Save(Guid uuid, PlayerState state) => writes.Enqueue(uuid, state);

    public bool TryLoad(Guid uuid, out PlayerState state) =>
        writes.TryPending(uuid, out state) || inner.TryLoad(uuid, out state);

    public void Dispose() {
        writes.Dispose();                 // drain queued writes first
        (inner as IDisposable)?.Dispose();
    }
}
