using System.Linq;

namespace SharpMinerals.Math;

/// <summary>A piecewise-linear spline over sorted (input, output) control points, clamped flat past the
/// ends. Maps a noise value (e.g. continentalness) to a target like terrain height, letting generators
/// shape that mapping with a few points instead of an algebraic curve. Point counts are tiny, so lookup is
/// a linear scan.</summary>
public sealed class Spline {
    readonly double[] inputs;
    readonly double[] outputs;

    public Spline(params (double Input, double Output)[] points) {
        if (points.Length == 0)
            throw new ArgumentException("a spline needs at least one control point", nameof(points));
        var sorted = points.OrderBy(p => p.Input).ToArray();
        inputs = new double[sorted.Length];
        outputs = new double[sorted.Length];
        for (int i = 0; i < sorted.Length; i++) {
            inputs[i] = sorted[i].Input;
            outputs[i] = sorted[i].Output;
        }
    }

    public double Sample(double input) {
        if (input <= inputs[0]) return outputs[0];
        if (input >= inputs[^1]) return outputs[^1];

        int i = 0;
        while (i < inputs.Length - 1 && input > inputs[i + 1]) i++;
        double t = MathUtil.InverseLerp(inputs[i], inputs[i + 1], input);
        return MathUtil.Lerp(outputs[i], outputs[i + 1], t);
    }
}
