using System.Numerics;

namespace SharpMinerals.Entities;
public abstract class Entity : ITickable {
    public Vector3 Position { get; set; }
    public Vector2 Rotation { get; set; }

    public abstract void Tick();
}
