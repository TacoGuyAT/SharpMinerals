using SharpMinerals.Math;

namespace SharpMinerals.Level.Generator.Biomes;

/// <summary>The composite density for a biome world. Terrain height is a shared continentalness spline (the
/// macro land/ocean shape) plus the neighbour-blended per-biome <c>BaseHeight</c>; the 3D field's amplitude is
/// the blended per-biome <c>HeightVariation</c>, scaled up where it is rocky; and each biome's optional detail
/// field is added in scaled by that biome's weight, so it fades to nothing at the border (unique terrain with
/// no muddy blends and no cliffs). All inputs come from the shared <see cref="BiomeSource"/>, so the selected
/// biome and the terrain shape always agree.</summary>
public sealed class BiomeDensity : IDensity {
    const double SeaLevel = WorldDefaults.SeaLevel;

    readonly BiomeSource source;
    readonly NoiseSampler density3d;

    // Continentalness [-1, 1] -> height offset from sea level: deep ocean, coast near sea level, then inland.
    static readonly Spline ContinentalSpline = new(
        (-1.0, -30.0), (-0.3, -8.0), (0.0, 2.0), (0.3, 10.0), (0.7, 22.0), (1.0, 30.0));

    public BiomeDensity(int seed, BiomeSource source) {
        this.source = source;
        density3d = new NoiseSampler(seed ^ 0x6F1E9A, frequency: 0.0125, octaves: 4);
    }

    /// <summary>The macro terrain height (sea level + continental spline + blended biome base) WITHOUT the 3D
    /// perturbation or contributions - a cheap estimate of where the surface sits, used to centre a tight
    /// surface scan during decoration.</summary>
    public double SurfaceHeight(int x, int z) {
        var climate = source.ClimateAt(x, z);
        Span<double> weights = stackalloc double[source.Biomes.Count];
        source.WeightsFor(climate, weights);
        double baseHeight = 0.0;
        for (int i = 0; i < weights.Length; i++) baseHeight += weights[i] * source.Biomes[i].BaseHeight;
        return SeaLevel + ContinentalSpline.Sample(climate.Continentalness) + baseHeight;
    }

    public double At(int x, int y, int z) {
        var climate = source.ClimateAt(x, z);
        Span<double> weights = stackalloc double[source.Biomes.Count];
        source.WeightsFor(climate, weights);

        double baseHeight = 0.0, amplitude = 0.0, contribution = 0.0;
        for (int i = 0; i < weights.Length; i++) {
            var biome = source.Biomes[i];
            baseHeight += weights[i] * biome.BaseHeight;
            amplitude += weights[i] * biome.HeightVariation;
            if (biome.Contribution is { } detail) contribution += weights[i] * detail.At(x, y, z);
        }

        // Rockier columns get a taller 3D field; clamp the rockiness term to the positive half so flat biomes stay flat.
        amplitude *= 1.0 + 0.6 * System.Math.Max(0.0, climate.Rockiness);

        double surface = SeaLevel + ContinentalSpline.Sample(climate.Continentalness) + baseHeight;
        return (surface - y) + density3d.Sample3D(x, y, z) * amplitude + contribution;
    }
}
