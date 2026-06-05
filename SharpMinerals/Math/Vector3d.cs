#if DOUBLE_PRECISION
global using Vector3m = SharpMinerals.Math.Vector3d;

namespace SharpMinerals.Math;
public struct Vector3d : IEquatable<Vector3d> {
    public Mfloat X;
    public Mfloat Y;
    public Mfloat Z;

    public Vector3d(double x, double y, double z) {
        X = x;
        Y = y;
        Z = z;
    }

    public bool Equals(Vector3d other) => X == other.X && Y == other.Y && Z == other.Z;
    public override bool Equals(object? obj) => obj is Vector3d other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(X, Y, Z);
    public static bool operator ==(Vector3d left, Vector3d right) => left.Equals(right);
    public static bool operator !=(Vector3d left, Vector3d right) => !left.Equals(right);
}
#else
global using Vector3m = System.Numerics.Vector3;
#endif

