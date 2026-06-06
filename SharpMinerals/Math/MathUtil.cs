namespace SharpMinerals.Math;

/// <summary>Small scalar helpers that System.Math does not provide directly, used across world generation
/// (interpolation, clamping, easing). Kept here so generators and splines share one implementation.</summary>
public static class MathUtil {
    /// <summary>Linear interpolation: a at t=0, b at t=1 (not clamped).</summary>
    public static double Lerp(double a, double b, double t) => a + (b - a) * t;

    public static double Clamp(double v, double min, double max) => v < min ? min : v > max ? max : v;

    /// <summary>The t such that Lerp(a, b, t) == v; 0 when a == b.</summary>
    public static double InverseLerp(double a, double b, double v) => a == b ? 0.0 : (v - a) / (b - a);

    /// <summary>Hermite smoothstep, clamped: 0 at or below 0, 1 at or above 1, eased in/out between.</summary>
    public static double Smoothstep(double t) {
        t = Clamp(t, 0.0, 1.0);
        return t * t * (3.0 - 2.0 * t);
    }

    /// <summary>Catmull-Rom cubic: the smooth curve through p1..p2 with neighbours p0,p3 (t in [0,1]). C1
    /// continuous across segments and exact for linear data - the building block for tricubic interpolation.</summary>
    public static double CatmullRom(double p0, double p1, double p2, double p3, double t) {
        double t2 = t * t, t3 = t2 * t;
        return 0.5 * (2.0 * p1
            + (-p0 + p2) * t
            + (2.0 * p0 - 5.0 * p1 + 4.0 * p2 - p3) * t2
            + (-p0 + 3.0 * p1 - 3.0 * p2 + p3) * t3);
    }
}
