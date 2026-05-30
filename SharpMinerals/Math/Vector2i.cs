namespace SharpMinerals.Math;
public struct Vector2i {
    public Mint X;
    public Mint Y;
    public Vector2i(Mint x, Mint y) {
        X = x;
        Y = y;
    }

    public static Vector2i operator +(Vector2i a, Vector2i b) => new Vector2i(a.X + b.X, a.Y + b.Y);
}
