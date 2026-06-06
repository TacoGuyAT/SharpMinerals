namespace SharpMinerals;

/// <summary>Rolling measured ticks-per-second over arbitrary time windows, in the spirit of load averages.
///
/// The game loop catches up after a stall: PrecisionTimer advances its schedule every tick and only sleeps
/// when it is ahead, so a stall is followed by a burst of back-to-back ticks that makes up the lost ones. A
/// naive "count ticks in the last N seconds" metric is fooled by that burst - it snaps straight back to the
/// target the instant the server recovers, hiding the lag.
///
/// To stay honest we bucket ticks per real second and CAP each bucket at the target. A catch-up burst lands
/// in one second's bucket and is clamped to the target; the stalled seconds keep their low (or zero) counts
/// and remain in the 1m/5m windows until they age out, so those windows recover gradually instead of
/// instantly. Timestamps are passed in (monotonic milliseconds), keeping the type clock-free and testable.</summary>
public sealed class TpsTracker {
    const int LongestWindowSeconds = 300; // 5 minutes - the ring holds at least this many one-second buckets

    readonly int[] buckets; // tick count per real second, indexed by a ring over the last (capacity) seconds
    readonly int capacity;
    readonly object gate = new();
    readonly double target;

    bool started;
    long headSecond; // absolute second (ms/1000) of the newest bucket
    long firstSecond; // absolute second of the very first recorded tick (so a young server divides by real age)
    int headPos;      // ring index of headSecond

    public TpsTracker(double targetTps) {
        target = targetTps;
        capacity = LongestWindowSeconds + 2;
        buckets = new int[capacity];
    }

    /// <summary>Records that a tick completed at <paramref name="nowMs"/> (monotonic milliseconds).</summary>
    public void Record(long nowMs) {
        lock (gate) {
            long second = nowMs / 1000;
            if (!started) {
                started = true;
                headSecond = firstSecond = second;
                headPos = 0;
                buckets[0] = 0;
            }
            Advance(second);
            buckets[headPos]++;
        }
    }

    /// <summary>Measured TPS over the last <paramref name="windowSeconds"/> as of <paramref name="nowMs"/>,
    /// capped at the target. Averages whole completed seconds (each capped), so a current catch-up burst can't
    /// inflate it; returns 0 before the first full second of ticks.</summary>
    public double Measure(long nowMs, double windowSeconds) {
        lock (gate) {
            if (!started) return 0.0;

            long now = nowMs / 1000;
            int window = (int)windowSeconds;
            double sum = 0.0;
            int counted = 0;
            // Walk back over completed seconds (skip the in-progress current second at i = 0).
            for (int i = 1; i <= window; i++) {
                long second = now - i;
                if (second < firstSecond) break;       // older than we have data for
                if (headSecond - second >= capacity) break; // fell off the ring
                double ticks = second > headSecond ? 0.0 // a second since our last tick = an ongoing stall
                    : System.Math.Min(buckets[((headPos - (int)(headSecond - second)) % capacity + capacity) % capacity], target);
                sum += ticks;
                counted++;
            }
            return counted == 0 ? 0.0 : System.Math.Min(sum / counted, target);
        }
    }

    /// <summary>Moves the head up to <paramref name="second"/>, zeroing the buckets for any seconds skipped
    /// (so stalled seconds are recorded as no-tick seconds, not silently merged with the next active one).</summary>
    void Advance(long second) {
        if (second <= headSecond) return;
        long gap = second - headSecond;
        if (gap >= capacity) {
            System.Array.Clear(buckets);
            buckets[headPos] = 0;
        } else {
            for (long k = 0; k < gap; k++) {
                headPos = (headPos + 1) % capacity;
                buckets[headPos] = 0;
            }
        }
        headSecond = second;
    }
}
