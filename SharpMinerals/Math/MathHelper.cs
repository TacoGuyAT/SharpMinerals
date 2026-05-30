#define USE64

#if USE64
global using Mint = long;
#else
global using Mint = int;
#endif

namespace SharpMinerals.Math;

/// <summary>Integer math helpers that behave correctly for negative coordinates.</summary>
public static class MathHelper {
    /// <summary>Floored integer division (rounds toward negative infinity).</summary>
    public static long FloorDiv(long a, long b) {
        long q = a / b;
        if ((a % b != 0) && ((a ^ b) < 0)) q--;
        return q;
    }

    /// <summary>Non-negative modulo for a positive divisor.</summary>
    public static int FloorMod(long a, long b) {
        long r = a % b;
        if (r != 0 && ((a ^ b) < 0)) r += b;
        return (int)r;
    }
}
