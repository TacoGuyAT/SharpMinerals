using SharpMinerals.Level;
using SharpMinerals.Level.Generator;
using SharpMinerals.Math;

namespace SharpMinerals.Vanilla.Generator;

/// <summary>The P1 overworld density field. A continentalness channel drives a base height through a spline
/// (lowlands -> hills -> mountains), broad depth noise adds metre-scale variation, and an FBm 3D field
/// perturbs the surface for roughness and gentle overhangs. A cell is solid below the resulting surface, air
/// above. Height is clamped to sea level for now: below-sea terrain and water fill arrive in the biome phase
/// once a water block exists. Seeded and stateless, so cubes generate independently and identically.</summary>
public sealed class OverworldDensity : IDensity {
    const double SeaLevel = WorldDefaults.SeaLevel;
    const double DensityAmplitude = 7.0; // vertical reach of the 3D roughness/overhang term, in blocks

    // Continentalness in [-1, 1] -> height offset above sea level.
    static readonly Spline HeightSpline = new(
        (-1.0, 0.0), (-0.2, 6.0), (0.2, 18.0), (0.6, 40.0), (1.0, 72.0));

    readonly NoiseSampler continentalness;
    readonly NoiseSampler depth;
    readonly NoiseSampler density3d;

    public OverworldDensity(int seed) {
        continentalness = new NoiseSampler(seed, frequency: 0.0009, octaves: 3);
        depth = new NoiseSampler(seed ^ 0x1b2c3d, frequency: 0.0125, octaves: 3);
        density3d = new NoiseSampler(seed ^ 0x6f1e9a, frequency: 0.0125, octaves: 4);
    }

    /// <summary>The terrain surface height (where the 2D shape crosses), before the 3D perturbation.</summary>
    public double SurfaceHeight(int x, int z) {
        double h = SeaLevel + HeightSpline.Sample(continentalness.Sample2D(x, z));
        h += depth.Sample2D(x, z) * 4.0;
        return h < SeaLevel ? SeaLevel : h;
    }

    public double At(int x, int y, int z) =>
        (SurfaceHeight(x, z) - y) + density3d.Sample3D(x, y, z) * DensityAmplitude;
}
