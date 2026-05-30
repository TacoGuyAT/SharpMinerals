namespace SharpMinerals.Entities.Components;

/// <summary>Per-tick movement delta integrated into <see cref="Transform"/>.</summary>
public struct Velocity {
    public double X, Y, Z;
    public Velocity(double x, double y, double z) { X = x; Y = y; Z = z; }
}
