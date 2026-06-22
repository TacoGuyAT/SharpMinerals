namespace SharpMinerals.Level.Generator.Biomes;

/// <summary>The climate authority for a world: it samples the five low-frequency climate axes and turns them
/// into soft per-biome weights (a Gaussian falloff in climate space, normalised to a partition of unity).
/// Those weights drive both the smooth height blend (in <see cref="BiomeDensity"/>) and the dithered surface
/// pick, so it is the single source the density and surface passes share - guaranteeing the ocean biome's low
/// continentalness lines up with the terrain dip. Stateless and seeded; thread-safe (weight buffers are
/// caller-supplied spans, no shared mutable state).</summary>
public sealed class BiomeSource {
    // Climate features are continental in scale, so the channels run at very low frequency.
    const double ClimateFrequency = 0.0006;
    const double ContinentalFrequency = 0.00035; // continents are the largest feature of all
    const double BlendSigma = 0.3;               // Gaussian width in climate space (smaller = sharper biomes)
    const double DitherSharpness = 20.0;         // sharpens the surface dither; band width ~ sigma/sqrt(this) - higher = narrower

    const double FeatureFrequency = 0.005; // feature-density patches (dense groves vs clearings) are mid-scale
    const double RiverFrequency = 0.002;   // winding rivers - finer than biomes, coarser than terrain detail

    readonly NoiseSampler temperature;
    readonly NoiseSampler humidity;
    readonly NoiseSampler continentalness;
    readonly NoiseSampler rockiness;
    readonly NoiseSampler weirdness;
    readonly NoiseSampler featureDensity;
    readonly NoiseSampler river;
    readonly NoiseSampler rockPatches; // high-frequency: scatters small bare-stone outcrops within rocky terrain
    readonly IBiome[] biomes;
    readonly IBiome? coastal;

    // A column is surfaced as beach only if it is a warm, near-sea-level LAND column with actual ocean within a
    // few blocks. The elevation window keeps sand at the water's edge (not on cliffs or deep ocean floor); the
    // ocean-proximity probe is what ties it to the shoreline - so beaches are thin strips hugging the ocean,
    // never large inland patches that merely happen to sit at sea-level elevation.
    const int BeachLow = WorldDefaults.SeaLevel - 2;   // wet sand a couple blocks under the waterline
    const int BeachHigh = WorldDefaults.SeaLevel + 2;  // ... up to a couple blocks above it (hug the water)
    const double BeachTempMin = 0.0;                   // medium to slightly-high temperature (no cold/snowy or scorched coasts)
    const double BeachTempMax = 0.55;
    const double BeachVegetationMax = 0.5;             // beaches form only where vegetation density is below this (open shores, not woods)
    const int CoastReach = 4;                           // actual water must lie within this many blocks for a beach
    const double RockPatchFrequency = 0.05;            // small bare-stone outcrops (high-frequency, ~tens of blocks)
    const double RockyMin = 0.15;                      // rockiness below this: no bare stone at all (soft/flat terrain)
    const double RockyMax = 0.70;                      // rockiness at which stone coverage maxes out
    const double RockMaxCoverage = 0.55;               // up to this fraction of the rockiest ground strips to stone

    // 8 compass directions, probed at CoastReach to find the actual water's edge (surface below sea level).
    static readonly (int dx, int dz)[] CoastDirs =
        { (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1) };

    // The cheap 2D surface height (BiomeDensity.SurfaceHeight), wired in after construction so the coastal check
    // can find the real waterline rather than a continentalness proxy. Null => no beaches (coastline unknown).
    Func<int, int, double>? heightAt;
    public void UseSurfaceHeight(Func<int, int, double> surfaceHeight) => heightAt = surfaceHeight;

    // ClimateAt is the generator's hottest noise path - the density lattice probes a column once per vertical
    // level, and the surface pick, river surface estimate and decorators all re-sample the same columns in later
    // passes - so each column's five climate channels are memoised once and shared across them all.
    readonly ThreadLocal<ClimateCache> climateCache = new(() => new ClimateCache());

    public IReadOnlyList<IBiome> Biomes => biomes;

    /// <summary>The shoreline biome placed by elevation rather than climate (null if no mod registered one). The
    /// surface shader needs its soil depth for the upward probe, so it is exposed.</summary>
    public IBiome? CoastalBiome => coastal;

    public BiomeSource(int seed, IReadOnlyList<IBiome> biomes, IBiome? coastal = null) {
        if (biomes.Count == 0)
            throw new ArgumentException("a biome source needs at least one biome", nameof(biomes));
        this.biomes = biomes.ToArray();
        this.coastal = coastal;
        temperature = new NoiseSampler(seed ^ 0x7E11A1, ClimateFrequency, octaves: 2);
        humidity = new NoiseSampler(seed ^ 0x40D17, ClimateFrequency, octaves: 2);
        continentalness = new NoiseSampler(seed ^ 0xC0A57, ContinentalFrequency, octaves: 3);
        rockiness = new NoiseSampler(seed ^ 0x12CC1, ClimateFrequency, octaves: 2);
        weirdness = new NoiseSampler(seed ^ 0x3B19D, ClimateFrequency, octaves: 2);
        featureDensity = new NoiseSampler(seed ^ 0xFEA7, FeatureFrequency, octaves: 2);
        river = new NoiseSampler(seed ^ 0x217E5, RiverFrequency, octaves: 2);
        rockPatches = new NoiseSampler(seed ^ 0x70CC5, RockPatchFrequency, octaves: 2);
    }

    /// <summary>The river field; terrain carves down to a riverbed where this is near zero, so its winding zero
    /// contour becomes a river once the water fill floods the channel.</summary>
    public double River(int x, int z) => river.Sample2D(x, z);

    /// <summary>A [0, 1] feature-density map: scales how thickly decorators (trees, flora) scatter at a column,
    /// so vegetation forms dense groves and open clearings within a biome instead of a uniform sprinkle.</summary>
    public double FeatureDensity(int x, int z) {
        double n = featureDensity.Sample2D(x, z) * 0.5 + 0.5; // [-1,1] -> [0,1]
        return n < 0.0 ? 0.0 : n > 1.0 ? 1.0 : n;
    }

    public ClimatePoint ClimateAt(int x, int z) {
        long key = ((long)(uint)x << 32) | (uint)z;
        var cache = climateCache.Value!;
        if (cache.TryGet(key, out var climate)) return climate;
        climate = ComputeClimate(x, z);
        cache.Put(key, climate);
        return climate;
    }

    ClimatePoint ComputeClimate(int x, int z) => new(
        temperature.Sample2D(x, z),
        humidity.Sample2D(x, z),
        continentalness.Sample2D(x, z),
        rockiness.Sample2D(x, z),
        weirdness.Sample2D(x, z));

    /// <summary>Normalised blend weights for the given climate. <paramref name="weights"/> length must equal
    /// <see cref="Biomes"/> count. Pure (no noise) so the density pass can sample climate once and reuse it.</summary>
    public void WeightsFor(in ClimatePoint climate, Span<double> weights) {
        double total = 0.0;
        for (int i = 0; i < biomes.Length; i++) {
            double d2 = ClimateDistanceSq(climate, biomes[i].Climate);
            double w = System.Math.Exp(-d2 / (2.0 * BlendSigma * BlendSigma));
            weights[i] = w;
            total += w;
        }
        if (total <= 0.0) { weights[0] = 1.0; return; } // degenerate (all astronomically far) - pick the first
        for (int i = 0; i < biomes.Length; i++) weights[i] /= total;
    }

    public void Weights(int x, int z, Span<double> weights) => WeightsFor(ClimateAt(x, z), weights);

    /// <summary>The dominant biome for an arbitrary climate (nearest in climate space). Used by the density's
    /// shaping callers and exposed for testing without going through the noise.</summary>
    public IBiome DominantForClimate(in ClimatePoint climate) {
        int best = 0;
        double bestD = double.MaxValue;
        for (int i = 0; i < biomes.Length; i++) {
            double d2 = ClimateDistanceSq(climate, biomes[i].Climate);
            if (d2 < bestD) { bestD = d2; best = i; }
        }
        return biomes[best];
    }

    public IBiome Dominant(int x, int z) => DominantForClimate(ClimateAt(x, z));

    /// <summary>The biome whose surface rule applies at this column, dithered across borders: each column is
    /// assigned a biome at random (by a stable per-column hash) in proportion to the biomes' blend weights, so
    /// the discrete surface block fades across a border as a gradient instead of a hard line. The weights are
    /// sharpened (raised to <see cref="DitherSharpness"/>) first, so a column resolves to a single biome quickly
    /// once away from the border - a smooth but fast transition, and almost no speckle inside a biome.</summary>
    public IBiome SurfacePick(int x, int z) {
        Span<double> weights = stackalloc double[biomes.Length];
        Weights(x, z, weights);

        double total = 0.0;
        for (int i = 0; i < weights.Length; i++) {
            weights[i] = System.Math.Pow(weights[i], DitherSharpness);
            total += weights[i];
        }

        double r = Hash01(x, z) * total; // proportional pick; scale the hash by total to skip a normalize pass
        double acc = 0.0;
        for (int i = 0; i < weights.Length; i++) {
            acc += weights[i];
            if (r < acc) return biomes[i];
        }
        return biomes[^1];
    }

    /// <summary>The biome whose surface rule applies at a column, given the column's top solid height. A warm,
    /// near-sea-level LAND column with ocean within <see cref="CoastReach"/> blocks is the coastal beach - so
    /// beaches are thin strips hugging the shoreline; every other column falls through to the dithered climate
    /// <see cref="SurfacePick"/>. The ocean-proximity probe (not just elevation) is what keeps beaches off inland
    /// sea-level terrain, and bounds their width to the reach instead of a broad continentalness band.</summary>
    public IBiome SurfaceBiomeAt(int x, int z, int surfaceTop) {
        if (coastal is not null && IsCoastalColumn(x, z, surfaceTop)) return coastal;
        return SurfacePick(x, z);
    }

    /// <summary>Whether a column is surfaced as the coastal beach (so decorators can skip it - e.g. no trees on
    /// sand). False if no coastal biome is registered.</summary>
    public bool IsCoastal(int x, int z, int surfaceTop) => coastal is not null && IsCoastalColumn(x, z, surfaceTop);

    /// <summary>Whether a column's soil is stripped to bare stone - a surface feature (not a biome): small exposed-
    /// stone outcrops, the alpha/beta look. Only rocky terrain shows any (gated by the low-frequency rockiness axis),
    /// but the stone itself is scattered by a HIGH-frequency patch noise so it appears as small patches rather than
    /// whole regions; coverage grows with rockiness up to <see cref="RockMaxCoverage"/>.</summary>
    public bool StripSoil(int x, int z) {
        double rock = ClimateAt(x, z).Rockiness;
        if (rock <= RockyMin) return false; // soft/flat terrain keeps its full soil
        double bias = (rock - RockyMin) / (RockyMax - RockyMin);
        bias = bias < 0.0 ? 0.0 : bias > 1.0 ? 1.0 : bias;     // how rocky this region is (0..1)
        double patch = rockPatches.Sample2D(x, z) * 0.5 + 0.5; // [0, 1] small high-frequency outcrops
        return patch > 1.0 - bias * RockMaxCoverage;           // rockier => more, but always scattered, stone patches
    }

    bool IsCoastalColumn(int x, int z, int surfaceTop) {
        if (surfaceTop < BeachLow || surfaceTop > BeachHigh) return false;          // only dry land at the water's-edge elevation
        double t = ClimateAt(x, z).Temperature;
        if (t < BeachTempMin || t > BeachTempMax) return false;                     // warm coasts only
        if (FeatureDensity(x, z) >= BeachVegetationMax) return false;               // wooded/grove shores stay green; sand only on open coast
        return WaterWithinReach(x, z);                                              // and the actual waterline must be right there
    }

    // True if any direction at CoastReach has its surface below sea level - i.e. open water (or a river) is adjacent.
    bool WaterWithinReach(int x, int z) {
        if (heightAt is null) return false; // no surface-height field wired -> can't locate the coastline
        foreach (var (dx, dz) in CoastDirs)
            if (heightAt(x + dx * CoastReach, z + dz * CoastReach) < WorldDefaults.SeaLevel) return true;
        return false;
    }

    static double ClimateDistanceSq(in ClimatePoint a, in ClimatePoint b) {
        double dt = a.Temperature - b.Temperature;
        double dh = a.Humidity - b.Humidity;
        double dc = a.Continentalness - b.Continentalness;
        double dr = a.Rockiness - b.Rockiness;
        double dw = a.Weirdness - b.Weirdness;
        // Continentalness separates land from ocean, so it weighs more than the softer climate axes.
        return dt * dt + dh * dh + 2.5 * dc * dc + dr * dr + dw * dw;
    }

    // A cheap deterministic hash of a column to [0, 1) for stable per-column surface dithering.
    static double Hash01(int x, int z) {
        uint h = (uint)(x * 374761393) ^ (uint)(z * 668265263);
        h = (h ^ (h >> 13)) * 1274126177u;
        h ^= h >> 16;
        return h / 4294967296.0;
    }

    /// <summary>A per-thread, direct-mapped memo of the climate at a column, keyed by packed (x, z). Memo-only and
    /// pure: a miss (empty or collided slot) just recomputes the seeded value, so a collision is never wrong - only
    /// a touch slower - and nothing depends on call order. Sized to hold several chunk footprints (256 columns each)
    /// so a chunk's whole vertical cube stack and its later passes reuse one evaluation per column. Thread-local, so
    /// it keeps the source's "no shared mutable state" thread-safety and stays deterministic.</summary>
    sealed class ClimateCache {
        const int Bits = 12;          // 4096 slots ~= 16 chunk column footprints
        const int Size = 1 << Bits;

        readonly long[] keys = new long[Size];
        readonly ClimatePoint[] values = new ClimatePoint[Size];
        readonly bool[] occupied = new bool[Size];

        public bool TryGet(long key, out ClimatePoint climate) {
            int slot = Slot(key);
            if (occupied[slot] && keys[slot] == key) { climate = values[slot]; return true; }
            climate = default;
            return false;
        }

        public void Put(long key, in ClimatePoint climate) {
            int slot = Slot(key);
            keys[slot] = key;
            values[slot] = climate;
            occupied[slot] = true;
        }

        // Fibonacci hash of the whole 64-bit key, so both packed coordinates mix into the top Bits bits.
        static int Slot(long key) => (int)((ulong)key * 0x9E3779B97F4A7C15UL >> (64 - Bits));
    }
}
