using SharpMinerals.Level;
using SharpMinerals.Level.Generator;

namespace SharpMinerals.Vanilla.Generator;

/// <summary>Badlands' signature shape: ridged noise lifted to the positive half so it only ever raises the
/// column, forming stepped mesa plateaus on top of the shared base terrain. As a per-biome contribution it is
/// added scaled by the badlands weight, so the plateaus fade out where badlands gives way to its neighbours.</summary>
public sealed class MesaContribution : IDensity {
    readonly NoiseSampler ridged;
    readonly double amplitude;

    public MesaContribution(int seed, double amplitude) {
        ridged = new NoiseSampler(seed ^ 0x4E5A, frequency: 0.009, octaves: 4,
                                  fractal: FastNoiseLite.FractalType.Ridged);
        this.amplitude = amplitude;
    }

    public double At(int x, int y, int z) {
        // Sample as a 2D field (constant y) so it raises whole columns into plateaus; clamp to >= 0 (no digging).
        double r = ridged.Sample3D(x, 0, z);
        return r > 0.0 ? r * amplitude : 0.0;
    }
}
