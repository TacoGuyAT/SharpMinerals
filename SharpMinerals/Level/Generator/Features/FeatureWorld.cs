using SharpMinerals.Level.Generator.Biomes;

namespace SharpMinerals.Level.Generator.Features;

/// <summary>The generation context a placement is bound to when it becomes a live decorator: the world seed (for
/// <see cref="WorldRng"/>), the <see cref="BiomeSource"/> (biomes, feature-density, coastline), the cheap 2D
/// <see cref="Heights"/> estimate, and the finished terrain <see cref="Density"/> field a density-derived target
/// scans for the surface. A placement is written seed-free and world-free (just WHAT and WHERE); the generator
/// supplies this once via <c>.Bind(world)</c>, so the same feature description drops into any world.</summary>
public sealed class FeatureWorld {
    public int Seed { get; }
    public BiomeSource Source { get; }
    public BiomeDensity Heights { get; }
    public IDensity Density { get; }

    /// <summary>The shared terrain-surface memo for density-derived (scatter) placement: one estimate and one scan
    /// per column, reused across every scatter feature and every cube that reaches the column.</summary>
    public TerrainSurfaceCache TerrainSurface { get; }

    public FeatureWorld(int seed, BiomeSource source, BiomeDensity heights, IDensity density) {
        Seed = seed;
        Source = source;
        Heights = heights;
        Density = density;
        TerrainSurface = new TerrainSurfaceCache(heights, density);
    }
}
