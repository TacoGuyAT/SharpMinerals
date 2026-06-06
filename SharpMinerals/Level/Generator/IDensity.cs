namespace SharpMinerals.Level.Generator;

/// <summary>A pure density field over world coordinates: a cell is solid where <see cref="At"/> &gt; 0 and
/// air otherwise. Being stateless lets each 16x16x16 cube evaluate it independently (in any order) and lets
/// surface rules re-sample it above the cube border. P1 is an FBm-perturbed heightfield; later a
/// min/max/main interpolated triad can drop in behind this same interface, and the biome phase composes a
/// per-biome blend as another <see cref="IDensity"/>.</summary>
public interface IDensity {
    double At(int x, int y, int z);
}
