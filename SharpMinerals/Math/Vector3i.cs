using SharpMinerals.Level;

namespace SharpMinerals.Math;

public struct Vector3i : IEquatable<Vector3i> {
    public Mint X;
    public Mint Y;
    public Mint Z;
    public Vector3i(Mint x, Mint y, Mint z) {
        X = x;
        Y = y;
        Z = z;
    }

    public static Vector3i operator +(Vector3i a, Vector3i b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vector3i operator -(Vector3i a, Vector3i b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    public static bool operator ==(Vector3i a, Vector3i b) => a.Equals(b);
    public static bool operator !=(Vector3i a, Vector3i b) => !a.Equals(b);

    // Value equality + a real hash so Vector3i works as a dictionary key for chunks.
    public readonly bool Equals(Vector3i other) => X == other.X && Y == other.Y && Z == other.Z;
    public override readonly bool Equals(object? obj) => obj is Vector3i v && Equals(v);
    public override readonly int GetHashCode() => HashCode.Combine(X, Y, Z);
    public override readonly string ToString() => $"({X}, {Y}, {Z})";
    public readonly Vector3i ToChunk() => new(
        this.X >> Chunk.Shifts,
        this.Y >> Chunk.Shifts,
        this.Z >> Chunk.Shifts
    );

    public readonly Vector3i ToBlock() => new(
        this.X << Chunk.Shifts,
        this.Y << Chunk.Shifts,
        this.Z << Chunk.Shifts
    );

    public readonly Vector3i ToLocal() => new(
        this.X & Chunk.Mask,
        this.Y & Chunk.Mask,
        this.Z & Chunk.Mask
    );
}
