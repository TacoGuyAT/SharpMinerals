using SharpMinerals.Math;

namespace SharpMinerals.Entities.Components;

/// <summary>A survival player's in-progress block dig. Set when a Start Digging action arrives and validated on
/// Finish Digging: the break is only applied once <see cref="RequiredTicks"/> have elapsed since
/// <see cref="StartTick"/>. <see cref="Active"/> is false when the player is not mining anything.</summary>
[Component]
public struct DiggingEntityComponent {
    public bool Active;
    public Vector3i Position;
    public long StartTick;
    public int RequiredTicks;
}
