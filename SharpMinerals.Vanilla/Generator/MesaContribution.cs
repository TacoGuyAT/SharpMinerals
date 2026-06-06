using SharpMinerals.Level;
using SharpMinerals.Level.Generator;
using SharpMinerals.Math;

namespace SharpMinerals.Vanilla.Generator;

/// <summary>Badlands' signature shape: a ridged 3D field lifted to its positive half (so it only ever raises the
/// column) that builds stepped mesa plateaus on top of the shared base terrain. The Y coordinate is snapped to
/// terrace bands, so the field is constant within each band and the walls come out stepped; the contribution
/// also eases off toward the base, so the mesa tapers in slightly at the bottom rather than sitting on a bulky
/// plinth. As a per-biome contribution it is added scaled by the badlands weight, fading out at biome borders.</summary>
public sealed class MesaContribution : IDensity {
    const double StepHeight = 4.0;     // terrace thickness (blocks)
    const double VerticalScale = 3.0;  // stretch Y in the noise so each terrace band is a distinct slice
    const double ShrinkRange = 36.0;   // height above sea level over which the mesa reaches full size
    const double ShrinkFloor = 0.55;   // ... shrinking to this fraction at the base (slightly slimmer toward the bottom)

    readonly NoiseSampler ridged;
    readonly NoiseSampler cellular;
    readonly double amplitude;

    public MesaContribution(int seed, double amplitude) {
        ridged = new NoiseSampler(seed ^ 0x4E5A, frequency: 0.009, octaves: 4,
                                  fractal: FastNoiseLite.FractalType.Ridged);
        cellular = new NoiseSampler(seed ^ 0x23AF, frequency: 0.05, octaves: 1,
                                  fractal: FastNoiseLite.FractalType.None,
                                  cellularReturnType: FastNoiseLite.CellularReturnType.CellValue,
                                  type: FastNoiseLite.NoiseType.Cellular);
        this.amplitude = amplitude;
    }

    public double At(int x, int y, int z) {
        // Snap Y to a terrace band so the noise is constant within it -> the mesa walls step instead of sloping.
        double band = System.Math.Floor(y / StepHeight) * StepHeight;
        double r = ridged.Sample3D(x, band * VerticalScale, z);
        //r += cellular.Sample2D(x, z) * StepHeight;
        r += cellular.Sample2D(x, z) * 0.5f;
        if(r <= 0.0) return 0.0; // only raise (no digging)

        // Ease the contribution off toward the bottom so the mesa tapers in at its base.
        double taper = MathUtil.Clamp((y - WorldDefaults.SeaLevel) / ShrinkRange, ShrinkFloor, 1.0);
        return r * amplitude * taper;
    }
}
