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
}
