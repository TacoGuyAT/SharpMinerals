using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace SharpMinerals.Persistence;

/// <summary>
/// A write-behind queue: callers hand off <c>(key, value)</c> snapshots and a single background
/// worker drains them to the underlying synchronous write, off the hot path. Same-key writes
/// <b>coalesce</b> to the latest value (fewer disk writes), and a pending map provides
/// <b>read-after-write</b> consistency before the worker has flushed. <see cref="Dispose"/> drains.
/// </summary>
internal sealed class WriteBehind<TKey, TValue> : IDisposable where TKey : notnull {
    static readonly ILogger Log = Logging.For("Persistence");

    readonly Action<TKey, TValue> write;
    readonly string what;
    readonly ConcurrentDictionary<TKey, TValue> pending = new();
    readonly Channel<TKey> queue = Channel.CreateUnbounded<TKey>(new UnboundedChannelOptions { SingleReader = true });
    readonly Task worker;

    public WriteBehind(Action<TKey, TValue> write, string what) {
        this.write = write;
        this.what = what;
        worker = Task.Run(DrainAsync);
    }

    /// <summary>Queues the latest value for a key (overwriting any not-yet-flushed value).</summary>
    public void Enqueue(TKey key, TValue value) {
        pending[key] = value;          // latest wins
        queue.Writer.TryWrite(key);
    }

    /// <summary>The not-yet-flushed value for a key, if any (read-after-write).</summary>
    public bool TryPending(TKey key, out TValue value) => pending.TryGetValue(key, out value!);

    async Task DrainAsync() {
        await foreach (var key in queue.Reader.ReadAllAsync()) {
            if (!pending.TryGetValue(key, out var value))
                continue; // a coalesced earlier pass already wrote the latest value
            try {
                write(key, value);
            } catch (Exception ex) {
                Log.LogError(ex, "Async {What} write failed for {Key}", what, key);
            }
            // Remove only if unchanged; a newer Enqueue leaves it for the key already re-queued.
            pending.TryRemove(new KeyValuePair<TKey, TValue>(key, value));
        }
    }

    public void Dispose() {
        queue.Writer.Complete();
        if (!worker.Wait(TimeSpan.FromSeconds(15)))
            Log.LogWarning("Async {What} writer did not drain within the timeout", what);
    }
}
