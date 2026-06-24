using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using SharpMinerals.Math;

namespace SharpMinerals.Level;

/// <summary>Loads chunks off the tick thread. Callers hand off a chunk coordinate via <see cref="Request"/> and a
/// pool of background workers run the (pure/IO) load-or-generate, then publish the finished chunk through the
/// supplied callback - the tick thread reads results with a non-blocking <c>TryGetLoaded</c>. Modeled on
/// <see cref="Persistence.WriteBehind{TKey,TValue}"/>: a <see cref="Channel{T}"/> work queue, a <c>pending</c> set
/// that dedups in-flight coordinates, and <see cref="Dispose"/> that drains. Safe because a generated chunk is
/// pure block data (block entities live in the chunk array, not the ECS), so publishing one touches no shared
/// simulation state - only the already-concurrent chunk map.</summary>
internal sealed class ChunkLoader : IDisposable {
    static readonly ILogger Log = Logging.For("Level.Chunks");

    readonly Func<Vector3i, Chunk> load;       // LoadOrGenerate: store fetch or generator, no ECS/sim state
    readonly Action<Vector3i, Chunk> publish;  // hand the finished chunk to the world (TryAdd into the chunk map)
    readonly ConcurrentDictionary<Vector3i, byte> pending = new();
    readonly Channel<Vector3i> queue = Channel.CreateUnbounded<Vector3i>();
    readonly ConcurrentDictionary<Vector3i, List<TaskCompletionSource<Chunk>>> waiters = new();
    readonly CancellationTokenSource cts = new();
    readonly Task[] workers;

    public ChunkLoader(Func<Vector3i, Chunk> load, Action<Vector3i, Chunk> publish, int? workerCount = null) {
        this.load = load;
        this.publish = publish;
        int n = workerCount ?? System.Math.Max(1, Environment.ProcessorCount - 2);
        workers = new Task[n];
        for (int i = 0; i < n; i++) workers[i] = Task.Run(DrainAsync);
    }

    /// <summary>Queues a chunk to load in the background unless it is already queued or in flight. The caller has
    /// already checked it is not loaded; combined with the publish using <c>TryAdd</c>, a stray duplicate can
    /// never overwrite a loaded (possibly edited) chunk. Returns whether a new request was enqueued.</summary>
    public bool Request(Vector3i pos) {
        if (!pending.TryAdd(pos, 0)) return false;       // already queued or generating
        if (queue.Writer.TryWrite(pos)) return true;
        pending.TryRemove(pos, out _);                   // writer completed (shutdown) - undo the reservation
        return false;
    }

    /// <summary>Awaitable form of <see cref="Request"/>: resolves once the chunk lands, whether this call started
    /// the load or just joined one already in flight. Never faults the queue's own workers - a failed load rejects
    /// only the waiters for that position.</summary>
    public Task<Chunk> RequestAsync(Vector3i pos) {
        var tcs = new TaskCompletionSource<Chunk>(TaskCreationOptions.RunContinuationsAsynchronously);
        var list = waiters.GetOrAdd(pos, _ => []);
        lock(list)
            list.Add(tcs);
        Request(pos); // no-op (returns false) if already pending - our waiter still rides the in-flight load
        return tcs.Task;
    }

    /// <summary>True while a coordinate is queued or generating - so eviction won't drop work about to land.</summary>
    public bool IsPending(Vector3i pos) => pending.ContainsKey(pos);

    public int PendingCount => pending.Count;

    async Task DrainAsync() {
        try {
            await foreach(var pos in queue.Reader.ReadAllAsync(cts.Token)) {
                Chunk? chunk = null;
                try {
                    chunk = load(pos);
                    publish(pos, chunk);
                } catch(Exception ex) {
                    Log.LogError(ex, "Async chunk load failed for {Pos}", pos);
                } finally {
                    pending.TryRemove(pos, out _);
                    if(waiters.TryRemove(pos, out var due)) {
                        lock(due) {
                            foreach(var t in due) {
                                if(chunk is not null) {
                                    t.TrySetResult(chunk);
                                } else {
                                    t.TrySetException(new Exception($"chunk load failed for {pos}"));
                                }
                            }
                        }
                    }
                }
            }
        } catch(OperationCanceledException) {
            // shutting down - drop the rest
        }
    }

    public void Dispose() {
        queue.Writer.TryComplete();
        cts.Cancel();
        try {
            if(!Task.WaitAll(workers, TimeSpan.FromSeconds(15))) {
                Log.LogWarning("Async chunk loader did not drain within the timeout");
            }
        } catch(AggregateException) {
            // worker faulted/cancelled during teardown - nothing left to do
        }
        cts.Dispose();
    }
}
