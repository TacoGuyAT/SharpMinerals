using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace SharpMinerals.Events;

/// <summary>A synchronous, type-keyed publish/subscribe bus scoped to a <see cref="Server"/>. Subscribers run
/// inline; a faulty one is logged and skipped. Subscriptions are copy-on-write, so publishing is lock-free.
/// Dispatch is polymorphic: publishing an event invokes handlers for its runtime type and every base type and
/// interface (so a derived event reaches base-type handlers). Events are reference types (use <c>record</c>).</summary>
public sealed class EventBus {
    static readonly ILogger Log = Logging.For("Events");

    readonly object gate = new();
    volatile Dictionary<Type, Action<object>[]> handlers = new();
    readonly ConcurrentDictionary<Type, Type[]> dispatchChains = new();
    readonly ConcurrentQueue<Action> deferred = new();

    /// <summary>Registers a handler for events assignable to <typeparamref name="T"/>.</summary>
    public void Subscribe<T>(Action<T> handler) where T : class {
        Action<object> wrapper = o => handler((T)o); // cast always valid by dispatch
        lock (gate) {
            var next = new Dictionary<Type, Action<object>[]>(handlers);
            next[typeof(T)] = next.TryGetValue(typeof(T), out var arr) ? [.. arr, wrapper] : [wrapper];
            handlers = next;
        }
    }

    /// <summary>Publishes an event to every subscriber of its type, base types, and interfaces.</summary>
    public void Publish(object e) {
        var map = handlers; // volatile snapshot
        foreach (var type in DispatchChain(e.GetType()))
            if (map.TryGetValue(type, out var arr))
                foreach (var handler in arr) {
                    try {
                        handler(e);
                    } catch (Exception ex) {
                        Log.LogWarning(ex, "Event handler for {Event} threw", type.Name);
                    }
                }
    }

    /// <summary>Queues an event to publish on the next <see cref="DrainDeferred"/> (tick thread), moving state
    /// mutations onto the single writer so they don't race the simulation / autosave.</summary>
    public void PublishDeferred(object e) => deferred.Enqueue(() => Publish(e));

    /// <summary>Queues arbitrary work to run on the next <see cref="DrainDeferred"/> (the tick thread).</summary>
    public void Defer(Action work) => deferred.Enqueue(work);

    /// <summary>Runs everything queued by <see cref="PublishDeferred"/> / <see cref="Defer"/> on the calling
    /// thread. Processes one generation per call (work queued during the drain runs next tick), so a
    /// self-feeding producer can't starve the loop.</summary>
    public void DrainDeferred() {
        for (int remaining = deferred.Count; remaining > 0 && deferred.TryDequeue(out var work); remaining--) {
            try {
                work();
            } catch (Exception ex) {
                Log.LogWarning(ex, "Deferred work threw");
            }
        }
    }

    // The concrete type plus all of its base classes (up to object) and interfaces - cached.
    // GetInterfaces over a runtime event type can't be statically proven trim-safe, but every interface that
    // matters here is an event contract a handler Subscribe<T>'d to, which roots it - so none are trimmed away.
    [UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "Event interfaces used for dispatch are preserved by their Subscribe<T> registrations.")]
    Type[] DispatchChain(Type concrete) => dispatchChains.GetOrAdd(concrete, static t => {
        var types = new List<Type>();
        for (var b = t; b is not null; b = b.BaseType) types.Add(b);
        types.AddRange(t.GetInterfaces());
        return types.ToArray();
    });
}
