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

    readonly NoiseSampler temperature;
    readonly NoiseSampler humidity;
    readonly NoiseSampler continentalness;
    readonly NoiseSampler rockiness;
    readonly NoiseSampler weirdness;
    readonly NoiseSampler featureDensity;
    readonly IBiome[] biomes;

    public IReadOnlyList<IBiome> Biomes => biomes;

    public BiomeSource(int seed, IReadOnlyList<IBiome> biomes) {
        if (biomes.Count == 0)
            throw new ArgumentException("a biome source needs at least one biome", nameof(biomes));
        this.biomes = biomes.ToArray();
        temperature = new NoiseSampler(seed ^ 0x7E11A1, ClimateFrequency, octaves: 2);
        humidity = new NoiseSampler(seed ^ 0x40D17, ClimateFrequency, octaves: 2);
        continentalness = new NoiseSampler(seed ^ 0xC0A57, ContinentalFrequency, octaves: 3);
        rockiness = new NoiseSampler(seed ^ 0x12CC1, ClimateFrequency, octaves: 2);
        weirdness = new NoiseSampler(seed ^ 0x3B19D, ClimateFrequency, octaves: 2);
        featureDensity = new NoiseSampler(seed ^ 0xFEA7, FeatureFrequency, octaves: 2);
    }

    /// <summary>A [0, 1] feature-density map: scales how thickly decorators (trees, flora) scatter at a column,
    /// so vegetation forms dense groves and open clearings within a biome instead of a uniform sprinkle.</summary>
    public double FeatureDensity(int x, int z) {
        double n = featureDensity.Sample2D(x, z) * 0.5 + 0.5; // [-1,1] -> [0,1]
        return n < 0.0 ? 0.0 : n > 1.0 ? 1.0 : n;
    }

    public ClimatePoint ClimateAt(int x, int z) => new(
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
}
