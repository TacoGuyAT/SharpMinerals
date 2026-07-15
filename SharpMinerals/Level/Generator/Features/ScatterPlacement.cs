using SharpMinerals.Level.Generator.Biomes;
using SharpMinerals.Math;

namespace SharpMinerals.Level.Generator.Features;

/// <summary>Scatters a feature on a jittered spacing-grid: one candidate per grid cell, nudged within the cell,
/// its surface found from the density field so trunks straddling a cube border derive identically from every cube
/// they reach into. Because scatter reaches across borders it can only use the block-blind
/// <see cref="PlacementTarget.Terrain"/> target (a neighbour cube's blocks do not exist yet), enforced at
/// <see cref="Bind"/>.</summary>
public sealed class ScatterPlacement : FeaturePlacement {
    readonly int spacing, jitter;

    public ScatterPlacement(Feature feature, int spacing, int jitter) : base(feature, PlacementTarget.Terrain) {
        this.spacing = spacing;
        this.jitter = jitter;
    }

    public override IChunkDecorator Bind(FeatureWorld world) {
        if (Target != PlacementTarget.Terrain)
            throw new InvalidOperationException(
                $"Scatter placement reaches across cube borders and can only use the {nameof(PlacementTarget.Terrain)} " +
                $"target (a neighbour cube's blocks are not available); got {Target}.");
        return new ScatterDecorator(Feature, new TerrainFinder(world), world.Seed, world.Source,
                                    Rule, RarityDensity, RarityFeatureMap, spacing, jitter);
    }

    /// <summary>The bound scatter driver: for each cube it walks the grid cells whose (jittered) candidate can
    /// reach the cube, rolls rarity, resolves the surface, gates on the rule, and stamps.</summary>
    sealed class ScatterDecorator : IChunkDecorator {
        // Salts for this driver's own rolls; features must avoid them for their own randomness.
        const int SaltJitterX = 0x10;
        const int SaltJitterZ = 0x20;
        const int SaltRarity = 0x7A;
        const int SurfaceBandTop = 140; // no land surface sits above this - lets sky cubes bail before the grid walk

        readonly Feature feature;
        readonly TerrainFinder finder;
        readonly int seed;
        readonly BiomeSource source;
        readonly SurfaceRule rule;
        readonly Func<IBiome, double>? rarityDensity;
        readonly bool rarityFeatureMap;
        readonly int spacing, jitter;

        public ScatterDecorator(Feature feature, TerrainFinder finder, int seed, BiomeSource source, SurfaceRule rule,
                                Func<IBiome, double>? rarityDensity, bool rarityFeatureMap, int spacing, int jitter) {
            this.feature = feature;
            this.finder = finder;
            this.seed = seed;
            this.source = source;
            this.rule = rule;
            this.rarityDensity = rarityDensity;
            this.rarityFeatureMap = rarityFeatureMap;
            this.spacing = spacing;
            this.jitter = jitter;
        }

        public void Decorate(Chunk chunk, Vector3i cube) {
            int baseX = (int)(cube.X * Chunk.Size);
            int baseY = (int)(cube.Y * Chunk.Size);
            int baseZ = (int)(cube.Z * Chunk.Size);
            int size = (int)Chunk.Size, up = feature.Extent.Up;

            // The feature lives in a band near the surface; skip cubes wholly below sea level or up in the sky.
            if (baseY + size <= WorldDefaults.SeaLevel || baseY > SurfaceBandTop + up) return;

            // Walk the spacing-grid cells whose (jittered) candidate could reach this cube.
            int reach = feature.Extent.Radius + jitter;
            int minGX = FloorDiv(baseX - reach, spacing), maxGX = FloorDiv(baseX + size - 1 + reach, spacing);
            int minGZ = FloorDiv(baseZ - reach, spacing), maxGZ = FloorDiv(baseZ + size - 1 + reach, spacing);

            for (int gz = minGZ; gz <= maxGZ; gz++)
                for (int gx = minGX; gx <= maxGX; gx++) {
                    var rng = WorldRng.At(seed, gx, gz);
                    int ox = gx * spacing + rng.Range(SaltJitterX, jitter + 1);
                    int oz = gz * spacing + rng.Range(SaltJitterZ, jitter + 1);

                    var biome = source.Dominant(ox, oz);
                    if (rarityDensity is not null) {
                        double density = rarityDensity(biome);
                        if (density <= 0.0) continue;
                        // The base density is per-column; a grid cell covers spacing^2 columns, so scale to a per-cell chance.
                        double perCell = density * spacing * spacing * (rarityFeatureMap ? source.FeatureDensity(ox, oz) : 1.0);
                        if (!rng.Chance(SaltRarity, perCell)) continue;
                    }

                    // Cheap 2D estimate first: skip candidates whose possible span can't reach this cube's y-range.
                    double est = finder.Estimate(ox, oz);
                    if (est + TerrainFinder.ScanMargin + 1 + up < baseY) continue;
                    if (est - TerrainFinder.ScanMargin + 1 >= baseY + size) continue;

                    if (!finder.TryAnchor(ox, oz, est, out var anchor)) continue;

                    var ctx = new PlaceContext(ox, oz, anchor, biome, source, rng);
                    if (!rule.Test(ctx)) continue;

                    feature.Place(ctx, new CubeSink(chunk, baseX, baseY, baseZ));
                }
        }

        static int FloorDiv(int a, int b) => a >= 0 ? a / b : -(((-a) + b - 1) / b);
    }
}

/// <summary>Resolves a column's surface from the terrain density field: a cheap 2D height estimate seeds a short
/// vertical scan for the true highest solid cell. Block-blind (a density field has no block type), so its anchors
/// carry air as the surface - callers that need the block use a <see cref="PlacementTarget.Solid"/> target. Works
/// at any column regardless of cube boundaries, which is why it is the border-safe scatter target.</summary>
public sealed class TerrainFinder {
    /// <summary>Half-height of the vertical scan window around the 2D estimate, in blocks.</summary>
    public const int ScanMargin = 16;

    readonly BiomeDensity heights;
    readonly IDensity density;

    public TerrainFinder(FeatureWorld world) {
        heights = world.Heights;
        density = world.Density;
    }

    /// <summary>The cheap 2D surface estimate at a column (the scan centre; also drives the cube-reach cull).</summary>
    public double Estimate(int x, int z) => heights.SurfaceHeight(x, z);

    /// <summary>Scans a <see cref="ScanMargin"/> window around <paramref name="estimate"/> for the highest solid
    /// cell. Returns false (leaving <paramref name="anchor"/> defaulted) when none is found in range.</summary>
    public bool TryAnchor(int x, int z, double estimate, out Anchor anchor) {
        int e = (int)estimate;
        for (int y = e + ScanMargin; y >= e - ScanMargin; y--)
            if (density.At(x, y, z) > 0) {
                anchor = new Anchor(y, CoreMod.Air);
                return true;
            }
        anchor = default;
        return false;
    }
}
