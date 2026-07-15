using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace SharpMinerals;
public sealed class Scheduler {
    static readonly ILogger Log = Logging.For("Scheduler"); 

    sealed class Timer(long next, long period, bool repeat, Action callback) {
        public long Next = next;
        public readonly long Period = period;
        public readonly bool Repeat = repeat;
        public readonly Action Callback = callback;
    }

    readonly ConcurrentQueue<Action> deferred = new();
    readonly List<Timer> timers = [];
    long tick;

    /// <summary>Whether any deferred work is queued (not yet drained by <see cref="Run"/>). Lets callers pump
    /// until quiescent - e.g. a test driving async work that self-defers its continuations.</summary>
    public bool HasDeferred => !deferred.IsEmpty;

    /// <summary>
    /// Defers work to run at the end of the tick or at the start of the next one.
    /// </summary>
    public void Defer(Action work) => deferred.Enqueue(work);

    /// <summary>
    /// Defers work to run at the end of the tick or at the start of the next one.
    /// Unlike <see cref="Defer(Action)"/>, it allows you to await current work and continue after it's finished.
    /// </summary>
    public Task DeferAsync(Action action) { 
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Defer(() => { try { action(); tcs.SetResult(); } catch(Exception ex) { tcs.SetException(ex); } });
        return tcs.Task;
    }

    /// <summary>
    /// Defers work to run at the end of the tick or at the start of the next one.
    /// Unlike <see cref="Defer(Action)"/>, it allows you to await current work and continue after it's finished.
    /// Unlike <see cref="DeferAsync(Action)"/>, it allows you to receive result of the work.
    /// </summary>
    public Task<T> DeferAsync<T>(Func<T> func) {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Defer(() => { try { tcs.SetResult(func()); } catch(Exception ex) { tcs.SetException(ex); } });
        return tcs.Task;
    }

    /// <summary>
    /// Lets you await <paramref name="ticks"/> amount of ticks.
    /// </summary>
    public Task Delay(int ticks, CancellationToken ct = default) {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sub = Later(ticks, () => tcs.TrySetResult());
        if(ct.CanBeCanceled)
            ct.Register(() => { sub.Dispose(); tcs.TrySetCanceled(); });
        return tcs.Task;
    }

    /// <summary>
    /// Runs work with a <paramref name="delay"/> tick delay.
    /// </summary>
    public IDisposable Later(long delay, Action work) => Add(delay, repeat: false, work);
    /// <summary>
    /// Runs work each <paramref name="period"/> ticks.
    /// </summary>
    public IDisposable Every(long period, Action work) => Add(period, repeat: true, work);

    /// <summary>
    /// Schedules cancellable work.
    /// </summary>
    Cancelable Add(long period, bool repeat, Action cb) {
        var t = new Timer(tick + period, period, repeat, cb);
        lock(timers)
            timers.Add(t);
        return new Cancelable(() => { lock(timers) timers.Remove(t); });
    }

    /// <summary>
    /// Drains all deferred work.
    /// </summary>
    public void Run() {
        for(int remaining = deferred.Count; remaining > 0 && deferred.TryDequeue(out var work); remaining--) {
            try { 
                work.Invoke();
            } catch(Exception ex) { 
                Log.LogError(ex, "Deferred work threw"); 
            }
        }

        List<Timer>? due = null;
        lock(timers) {
            foreach(var t in timers) {
                if(t.Next <= tick) {
                    (due ??= []).Add(t);
                }
            }

            if(due is not null) {
                foreach(var t in due) {
                    if(t.Repeat) {
                        t.Next = tick + t.Period;
                    } else {
                        timers.Remove(t);
                    } 
                }
            }
        }

        tick++;

        if(due is null) {
            return;
        }

        foreach(var t in due) { 
            try { 
                t.Callback(); 
            } catch(Exception ex) { 
                Log.LogError(ex, "Scheduled callback threw"); 
            } 
        }
    }
}

sealed class Cancelable(Action dispose) : IDisposable { public void Dispose() => dispose.Invoke(); }