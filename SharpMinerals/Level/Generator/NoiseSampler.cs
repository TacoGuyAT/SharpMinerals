namespace SharpMinerals.Level.Generator;

/// <summary>One configured, seeded noise channel built on <see cref="FastNoiseLite"/>. Outputs are roughly
/// [-1, 1]. Each channel (continentalness, depth, the 3D field, later the climate axes) takes its own seed -
/// derive per-channel seeds from the world seed so the channels are independent. The same configuration
/// serves both 2D and 3D sampling.</summary>
public sealed class NoiseSampler {
    readonly FastNoiseLite noise;

    public NoiseSampler(int seed, double frequency, int octaves = 1,
                        FastNoiseLite.NoiseType type = FastNoiseLite.NoiseType.OpenSimplex2) {
        noise = new FastNoiseLite(seed);
        noise.SetNoiseType(type);
        noise.SetFrequency((float)frequency);
        noise.SetFractalType(octaves > 1 ? FastNoiseLite.FractalType.FBm : FastNoiseLite.FractalType.None);
        noise.SetFractalOctaves(octaves);
    }

    public double Sample2D(double x, double z) => noise.GetNoise((float)x, (float)z);
    public double Sample3D(double x, double y, double z) => noise.GetNoise((float)x, (float)y, (float)z);
}
