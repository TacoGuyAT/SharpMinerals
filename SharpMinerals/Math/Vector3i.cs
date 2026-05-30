namespace SharpMinerals.Math;
public struct Vector3i {
    public Mint X;
    public Mint Y;
    public Mint Z;
    public Vector3i(Mint x, Mint y, Mint z) {
        X = x;
        Y = y;
        Z = z;
    }

    public static Vector3i operator +(Vector3i a, Vector3i b) => new Vector3i(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
}