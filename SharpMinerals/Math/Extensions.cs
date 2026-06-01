#define DOUBLE_PRECISION

#if DOUBLE_PRECISION
global using Mint = long;
global using Mfloat = double;
#else
global using Mint = int;
global using Mfloat = float;
#endif

namespace SharpMinerals.Math;

public static class Extensions {
    public static Mfloat NextFloat(this Random random) =>
#if DOUBLE_PRECISION
        random.NextDouble();
#else
        random.NextSingle();
#endif
}