#if DOUBLE_PRECISION
global using Vector3m = SharpMinerals.Math.Vector3d;

namespace SharpMinerals.Math;
public struct Vector3d {
    public Mfloat X;
    public Mfloat Y;
    public Mfloat Z;

    public Vector3d(double x, double y, double z) {
        X = x;
        Y = y;
        Z = z;
    }
}
#else
global using Vector3m = System.Numerics.Vector3;
#endif

